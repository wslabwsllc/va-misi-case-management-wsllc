using System.Diagnostics;
using System.Net;
using System.Text;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.VeteranLookup.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>E4-S3: every outcome of the typed client, with the real resilience pipeline and a scripted transport.</summary>
public class VeteranLookupClientTests
{
    private const string FoundBody = """{"name":"Avery Ashgrove","address":"100 Sample Street, Fernwick, VA 00010"}""";

    private static (IVeteranLookupClient Client, StubHandler Handler, FakeLogCollector Logs, ServiceProvider Provider) Build(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond,
        TestCurrentUser? user = null,
        TimeSpan? attemptTimeout = null,
        TimeSpan? totalTimeout = null)
    {
        var handler = new StubHandler(respond);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VeteranLookup:BaseUrl"] = "http://lookup.test/prefix",
            ["VeteranLookup:AttemptTimeout"] = (attemptTimeout ?? TimeSpan.FromSeconds(2)).ToString(),
            ["VeteranLookup:TotalTimeout"] = (totalTimeout ?? TimeSpan.FromSeconds(5)).ToString(),
            ["VeteranLookup:RetryDelay"] = TimeSpan.FromMilliseconds(10).ToString(),
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ICurrentUser>(user ?? new TestCurrentUser(Permissions.VeteranLookup))
            .AddFakeLogging();
        VeteranLookupModule.AddVeteranLookupHttpClient(services).ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IVeteranLookupClient>(), handler, provider.GetFakeLogCollector(), provider);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string? json = null) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
        });

    [Fact]
    public async Task A_200_with_name_and_address_is_Found_and_the_request_matches_the_contract()
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody));
        await using var _ = provider;

        var result = await client.LookupAsync("900123456", CancellationToken.None);

        Assert.Equal(new VeteranLookupResult(VeteranLookupOutcome.Found, "Avery Ashgrove", "100 Sample Street, Fernwick, VA 00010"), result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://lookup.test/prefix/veterans/lookup", request.Uri!.ToString());   // base path prefix kept
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("""{"ssn":"900-12-3456"}""", request.Body);                                 // nine digits normalized
    }

    [Fact]
    public async Task A_404_is_NotFound()
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.NotFound));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.NotFound, (await client.LookupAsync("999-99-9999", CancellationToken.None)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("123-456-789")]
    [InlineData("123 45 6789")]
    [InlineData("12345678a")]
    [InlineData(" 900-12-3456")]
    [InlineData("９００１２３４５６")]          // full-width digits are not ASCII
    [InlineData("900123456\n")]           // "$" would accept a trailing newline
    [InlineData("900-12-3456\n")]
    public async Task A_malformed_SSN_is_InvalidInput_and_no_request_is_sent(string ssn)
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.InvalidInput, (await client.LookupAsync(ssn, CancellationToken.None)).Outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_user_without_the_lookup_permission_is_Forbidden_without_a_request_and_the_denial_is_logged()
    {
        var (client, handler, logs, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody), new TestCurrentUser(Permissions.CasesView));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Forbidden, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(0, handler.Calls);
        var denial = Assert.Single(logs.WithEventId(SecurityEvents.AuthorizationDeniedId));
        Assert.Equal(Permissions.VeteranLookup, denial.GetStructuredStateValue("Permission"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Retryable_statuses_are_retried_exactly_once_then_Unavailable(HttpStatusCode status)
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(status));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_transient_failure_followed_by_success_is_Found()
    {
        var (client, handler, _, provider) = Build((_, attempt, _) =>
            attempt == 1 ? Respond(HttpStatusCode.ServiceUnavailable) : Respond(HttpStatusCode.OK, FoundBody));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Found, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Unexpected_non_retryable_statuses_are_Unavailable_without_a_retry(HttpStatusCode status)
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(status));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"name":"Avery Ashgrove"}""")]
    [InlineData("""{"name":"","address":"100 Sample Street"}""")]
    [InlineData("""{"name":"Avery Ashgrove","address":null}""")]
    [InlineData("""{"name":123,"address":"100 Sample Street"}""")]
    [InlineData("null")]
    public async Task An_unusable_200_body_is_Unavailable(string body)
    {
        var (client, _, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, body));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_connection_dropped_while_the_body_is_read_is_Unavailable()
    {
        var (client, _, _, provider) = Build((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new DroppingStream()) { Headers = { ContentType = new("application/json") } },
        }));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_body_that_stalls_after_the_headers_is_Unavailable_within_the_total_budget()
    {
        var (client, _, _, provider) = Build((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream()) { Headers = { ContentType = new("application/json") } },
        }), attemptTimeout: TimeSpan.FromMilliseconds(200), totalTimeout: TimeSpan.FromMilliseconds(500));
        await using var _ = provider;
        var stopwatch = Stopwatch.StartNew();

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task An_undecodable_charset_is_Unavailable()
    {
        var (client, _, _, provider) = Build((_, _, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(FoundBody));
            content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=bogus");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData("veteran-lookup:8081")]
    [InlineData("localhost:8081")]
    [InlineData("/lookup")]
    [InlineData("ftp://lookup.test")]
    [InlineData("")]
    public void A_base_URL_that_is_not_absolute_http_fails_at_startup(string baseUrl) =>
        AssertInvalidAtStartup(new() { ["VeteranLookup:BaseUrl"] = baseUrl });

    [Theory]
    [InlineData("VeteranLookup:AttemptTimeout", "00:00:00.005")]
    [InlineData("VeteranLookup:AttemptTimeout", "00:00:00")]
    [InlineData("VeteranLookup:TotalTimeout", "00:00:01")]          // shorter than the 2 s attempt default
    [InlineData("VeteranLookup:RetryDelay", "-00:00:01")]
    public void Timeouts_outside_what_the_pipeline_accepts_fail_at_startup(string key, string value) =>
        AssertInvalidAtStartup(new() { ["VeteranLookup:BaseUrl"] = "http://lookup.test", [key] = value });

    private static void AssertInvalidAtStartup(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddLogging().AddVeteranLookupClient(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<VeteranLookupClientOptions>>().Value);
    }

    private sealed class StallingStream : MemoryStream
    {
        public StallingStream() : base(Encoding.UTF8.GetBytes("{\"name\":\"Av"))
        {
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= Length)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class DroppingStream : MemoryStream
    {
        public DroppingStream() : base(Encoding.UTF8.GetBytes("{\"name\":\"Av"))
        {
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position >= Length ? throw new HttpIOException(HttpRequestError.ResponseEnded) : base.ReadAsync(buffer, cancellationToken);
    }

    [Fact]
    public async Task A_name_longer_than_the_snapshot_limit_is_Unavailable()
    {
        var body = $$"""{"name":"{{new string('n', 201)}}","address":"100 Sample Street"}""";
        var (client, _, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, body));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_connection_failure_is_retried_once_then_Unavailable()
    {
        var (client, handler, _, provider) = Build((_, _, _) => throw new HttpRequestException("connection refused"));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_hanging_service_is_Unavailable_within_the_total_budget_after_one_retry()
    {
        var (client, handler, _, provider) = Build(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }, attemptTimeout: TimeSpan.FromMilliseconds(100), totalTimeout: TimeSpan.FromMilliseconds(400));
        await using var _ = provider;
        var stopwatch = Stopwatch.StartNew();

        var result = await client.LookupAsync("900-12-3456", CancellationToken.None);

        Assert.Equal(VeteranLookupOutcome.Unavailable, result.Outcome);
        Assert.Equal(2, handler.Calls);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task The_total_timeout_caps_the_whole_call_even_when_attempts_could_run_longer()
    {
        var (client, handler, _, provider) = Build(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }, attemptTimeout: TimeSpan.FromMilliseconds(300), totalTimeout: TimeSpan.FromMilliseconds(300));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_reporting_an_outage()
    {
        var (client, _, logs, provider) = Build(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });
        await using var _ = provider;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LookupAsync("900-12-3456", cts.Token));
        Assert.Empty(logs.WithEventId(4002));
    }

    [Fact]
    public async Task Logs_record_outcome_and_duration_but_never_the_SSN_or_veteran_details()
    {
        var (client, _, logs, provider) = Build((_, attempt, _) => attempt switch
        {
            1 => Respond(HttpStatusCode.OK, FoundBody),
            2 => Respond(HttpStatusCode.NotFound),
            _ => throw new HttpRequestException("POST http://lookup.test/veterans/lookup ssn=900-12-3456 failed"),
        });
        await using var _ = provider;

        await client.LookupAsync("900-12-3456", CancellationToken.None);
        await client.LookupAsync("900123457", CancellationToken.None);
        await client.LookupAsync("900-12-3458", CancellationToken.None);
        await client.LookupAsync("bad", CancellationToken.None);

        Assert.Equal(["Found", "NotFound"], logs.WithEventId(4001).Select(r => (string?)r.GetStructuredStateValue("Outcome")));
        Assert.NotNull(Assert.Single(logs.WithEventId(4001), r => (string?)r.GetStructuredStateValue("Outcome") == "Found").GetStructuredStateValue("DurationMs"));
        Assert.Single(logs.WithEventId(4002));
        Assert.Single(logs.WithEventId(4003));
        Assert.Equal("HttpRequestException", Assert.Single(logs.WithEventId(4004)).GetStructuredStateValue("Reason"));
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Category?.StartsWith("Polly", StringComparison.Ordinal) == true);
        // Hyphenated fragments, not bare digits: a logged duration such as 13.3457 ms would otherwise match by chance.
        LogAssertions.ContainsNoSensitiveValues(logs, "Avery", "Ashgrove", "Sample Street", "12-3456", "12-3457", "12-3458");
    }

    [Theory]
    [InlineData("900123456", "900-12-3456")]
    [InlineData("900-12-3456", "900-12-3456")]
    [InlineData("900-123456", null)]
    [InlineData(null, null)]
    public void Normalization_accepts_exactly_two_forms(string? input, string? expected)
    {
        Assert.Equal(expected, VeteranLookupClient.Normalize(input));
    }
}
