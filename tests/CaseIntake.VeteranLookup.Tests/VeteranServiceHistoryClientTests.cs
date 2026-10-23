using System.Diagnostics;
using System.Net;
using System.Text;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using CaseIntake.VeteranLookup.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// E4-C2-3: every outcome of the Service History client, with the real resilience pipeline and a scripted transport.
/// The permission and log cases are E3-C2-1 and E3-C2-2.
/// </summary>
[Category("Story:E4-C2-3")]
[Category("Story:E3-C2-1")]
[Category("Req:R-CR2-1")]
[Category("Req:R-CR2-4")]
[Category("Req:R-CR2-5")]
public class VeteranServiceHistoryClientTests
{
    private const string FoundBody =
        """{"branch":"Army","serviceStartDate":"2001-02-15","serviceEndDate":"2005-02-14","characterOfService":"Honorable","lastVerifiedDate":"2026-01-05"}""";

    private static readonly ServiceHistory Army =
        new("Army", new DateOnly(2001, 2, 15), new DateOnly(2005, 2, 14), "Honorable", new DateOnly(2026, 1, 5));

    private static (IVeteranServiceHistoryClient Client, StubHandler Handler, FakeLogCollector Logs, ServiceProvider Provider) Build(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond,
        TestCurrentUser? user = null,
        TimeSpan? attemptTimeout = null,
        TimeSpan? totalTimeout = null)
    {
        var handler = new StubHandler(respond);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VeteranHistory:BaseUrl"] = "http://history.test/prefix",
            ["VeteranHistory:AttemptTimeout"] = (attemptTimeout ?? TimeSpan.FromSeconds(2)).ToString(),
            ["VeteranHistory:TotalTimeout"] = (totalTimeout ?? TimeSpan.FromSeconds(5)).ToString(),
            ["VeteranHistory:RetryDelay"] = TimeSpan.FromMilliseconds(10).ToString(),
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ICurrentUser>(user ?? new TestCurrentUser(Permissions.VeteranLookup))
            .AddFakeLogging();
        VeteranLookupModule.AddServiceHistoryHttpClient(services).ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IVeteranServiceHistoryClient>(), handler, provider.GetFakeLogCollector(), provider);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string? json = null) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
        });

    [Test]
    public async Task A_200_with_a_storable_history_is_Found_and_the_request_carries_only_the_normalized_SSN()
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody));
        await using var _ = provider;

        var result = await client.LookupAsync("900123456", CancellationToken.None);

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.Found, Army), result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://history.test/prefix/veterans/service-history/lookup", request.Uri!.ToString());   // base path prefix kept
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("""{"ssn":"900-12-3456"}""", request.Body);
    }

    [Test]
    public async Task The_Government_example_without_last_verified_or_end_date_is_Found()
    {
        var (client, _, _, provider) = Build((_, _, _) =>
            Respond(HttpStatusCode.OK, """{"branch":"Army","serviceStartDate":"2001-02-15","characterOfService":"Honorable"}"""));
        await using var _ = provider;

        var result = await client.LookupAsync("900-12-3456", CancellationToken.None);

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.Found, Army with { ServiceEndDate = null, LastVerifiedOn = null }), result);
    }

    [Test]
    public async Task Equal_start_and_end_dates_are_Found()
    {
        var (client, _, _, provider) = Build((_, _, _) =>
            Respond(HttpStatusCode.OK, """{"branch":"Navy","serviceStartDate":"2001-02-15","serviceEndDate":"2001-02-15","characterOfService":"Honorable"}"""));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Found, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
    }

    [Test]
    public async Task A_404_is_NotFound()
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.NotFound));
        await using var _ = provider;

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.NotFound, null), await client.LookupAsync("902-17-3062", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [TestCase("")]
    [TestCase("12345678")]
    [TestCase("123-456-789")]
    [TestCase(" 900-12-3456")]
    [TestCase("900123456\n")]
    public async Task A_malformed_SSN_is_InvalidInput_and_no_request_is_sent(string ssn)
    {
        var (client, handler, logs, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.InvalidInput, (await client.LookupAsync(ssn, CancellationToken.None)).Outcome);
        Assert.Equal(0, handler.Calls);
        Assert.Single(logs.WithEventId(4012));
    }

    /// <summary>E3-C2-1: the service-history lookup is governed by the existing veteran.lookup permission (D5).</summary>
    [TestCase(Permissions.CasesView)]
    [TestCase(Permissions.CasesCreate)]
    [TestCase(Permissions.AuditView)]
    public async Task A_user_without_veteran_lookup_is_Forbidden_without_a_request_and_the_denial_is_logged(string onlyPermission)
    {
        var (client, handler, logs, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody), new TestCurrentUser(onlyPermission));
        await using var _ = provider;

        var result = await client.LookupAsync("900-12-3456", CancellationToken.None);

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.Forbidden, null), result);
        Assert.Equal(0, handler.Calls);
        var denial = Assert.Single(logs.WithEventId(SecurityEvents.AuthorizationDeniedId));
        Assert.Equal(SecurityEvents.AuthorizationDeniedName, denial.Id.Name);
        Assert.Equal(Permissions.VeteranLookup, denial.GetStructuredStateValue("Permission"));
        Assert.Equal("test-user", denial.GetStructuredStateValue("UserId"));
        Assert.Empty(logs.WithEventId(4012));   // the permission is checked before the SSN format
    }

    [Test]
    public async Task The_permission_is_checked_before_the_SSN_format()
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody), new TestCurrentUser(Permissions.CasesView));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Forbidden, (await client.LookupAsync("not an ssn", CancellationToken.None)).Outcome);
        Assert.Equal(0, handler.Calls);
    }

    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.RequestTimeout)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    public async Task Retryable_statuses_are_retried_exactly_once_then_Unavailable(HttpStatusCode status)
    {
        var (client, handler, logs, provider) = Build((_, _, _) => Respond(status));
        await using var _ = provider;

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.Unavailable, null), await client.LookupAsync("962-91-6025", CancellationToken.None));
        Assert.Equal(2, handler.Calls);
        Assert.Single(logs.WithEventId(4013));
        Assert.Equal(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Assert.Single(logs.WithEventId(4011)).GetStructuredStateValue("StatusCode"));
    }

    [Test]
    public async Task A_transient_503_followed_by_success_is_Found()
    {
        var (client, handler, _, provider) = Build((_, attempt, _) =>
            attempt == 1 ? Respond(HttpStatusCode.ServiceUnavailable) : Respond(HttpStatusCode.OK, FoundBody));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Found, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.NoContent)]
    public async Task Other_statuses_are_Unavailable_without_a_retry(HttpStatusCode status)
    {
        var (client, handler, _, provider) = Build((_, _, _) => Respond(status));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [TestCase("not json", TestName = "Malformed JSON")]
    [TestCase("{", TestName = "Truncated JSON")]
    [TestCase("null", TestName = "Null body")]
    [TestCase("[]", TestName = "Array body")]
    [TestCase("{}", TestName = "Empty object")]
    [TestCase("""{"serviceStartDate":"2001-02-15","characterOfService":"Honorable"}""", TestName = "Missing branch")]
    [TestCase("""{"branch":"","serviceStartDate":"2001-02-15","characterOfService":"Honorable"}""", TestName = "Blank branch")]
    [TestCase("""{"branch":"Army","characterOfService":"Honorable"}""", TestName = "Missing start date")]
    [TestCase("""{"branch":"Army","serviceStartDate":"2001-02-15"}""", TestName = "Missing character of service")]
    [TestCase("""{"branch":"Army","serviceStartDate":"02-15-2001","characterOfService":"Honorable"}""", TestName = "Bad date format")]
    [TestCase("""{"branch":"Army","serviceStartDate":"2001-02-30","characterOfService":"Honorable"}""", TestName = "Impossible date")]
    [TestCase("""{"branch":"Army","serviceStartDate":"2005-02-15","serviceEndDate":"2001-02-14","characterOfService":"Honorable"}""", TestName = "Start after end")]
    [TestCase("""{"branch":123,"serviceStartDate":"2001-02-15","characterOfService":"Honorable"}""", TestName = "Non-string branch")]
    public async Task An_unusable_200_body_is_Unavailable(string body)
    {
        var (client, _, logs, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, body));
        await using var _ = provider;

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.Unavailable, null), await client.LookupAsync("900-12-3456", CancellationToken.None));
        Assert.Equal("UnusableResponse", Assert.Single(logs.WithEventId(4011)).GetStructuredStateValue("Reason"));
    }

    [Test]
    public async Task A_branch_longer_than_the_snapshot_limit_is_Unavailable()
    {
        var body = $$"""{"branch":"{{new string('b', CaseFieldLimits.ServiceBranchMaxLength + 1)}}","serviceStartDate":"2001-02-15","characterOfService":"Honorable"}""";
        var (client, _, _, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, body));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
    }

    [Test]
    public async Task A_body_over_the_size_cap_is_Unavailable_even_when_it_is_valid()
    {
        // Valid JSON padded with whitespace: without the cap this would be Found.
        var body = FoundBody + new string(' ', VeteranLookupModule.MaxResponseBytes);
        var (client, _, logs, provider) = Build((_, _, _) => Respond(HttpStatusCode.OK, body));
        await using var _ = provider;

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.Unavailable, null), await client.LookupAsync("900-12-3456", CancellationToken.None));
        Assert.Equal("HttpRequestException", Assert.Single(logs.WithEventId(4011)).GetStructuredStateValue("Reason"));
    }

    [Test]
    public async Task A_connection_failure_is_retried_once_then_Unavailable()
    {
        var (client, handler, logs, provider) = Build((_, _, _) => throw new HttpRequestException("connection refused"));
        await using var _ = provider;

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.Equal(2, handler.Calls);
        Assert.Equal("HttpRequestException", Assert.Single(logs.WithEventId(4011)).GetStructuredStateValue("Reason"));
    }

    [Test]
    public async Task A_hanging_service_is_Unavailable_within_the_total_budget()
    {
        var (client, _, _, provider) = Build(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }, attemptTimeout: TimeSpan.FromMilliseconds(500), totalTimeout: TimeSpan.FromSeconds(2));
        await using var _ = provider;
        var stopwatch = Stopwatch.StartNew();

        Assert.Equal(VeteranLookupOutcome.Unavailable, (await client.LookupAsync("900-12-3456", CancellationToken.None)).Outcome);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    [Test]
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
        Assert.Empty(logs.WithEventId(4011));
    }

    [TestCase("veteran-history:8082")]
    [TestCase("/history")]
    [TestCase("ftp://history.test")]
    [TestCase("")]
    public void A_base_URL_that_is_not_absolute_http_fails_at_startup(string baseUrl) =>
        AssertInvalidAtStartup(new() { ["VeteranHistory:BaseUrl"] = baseUrl });

    [TestCase("VeteranHistory:AttemptTimeout", "00:00:00.005")]
    [TestCase("VeteranHistory:TotalTimeout", "00:00:01")]          // shorter than the 2 s attempt default
    [TestCase("VeteranHistory:RetryDelay", "-00:00:01")]
    public void Timeouts_outside_what_the_pipeline_accepts_fail_at_startup(string key, string value) =>
        AssertInvalidAtStartup(new() { ["VeteranHistory:BaseUrl"] = "http://history.test", [key] = value });

    private static void AssertInvalidAtStartup(Dictionary<string, string?> settings)
    {
        settings["VeteranLookup:BaseUrl"] = "http://lookup.test";   // the Lookup half is valid; only VeteranHistory is wrong
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddLogging().AddVeteranLookupClient(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<VeteranHistoryClientOptions>>().Value);
    }

    [Test]
    public void AddVeteranLookupClient_registers_both_clients()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VeteranLookup:BaseUrl"] = "http://lookup.test",
            ["VeteranHistory:BaseUrl"] = "http://history.test",
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddLogging().AddVeteranLookupClient(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<VeteranServiceHistoryClient>(provider.GetRequiredService<IVeteranServiceHistoryClient>());
        Assert.IsType<VeteranLookupClient>(provider.GetRequiredService<IVeteranLookupClient>());
    }

    /// <summary>
    /// E3-C2-2: events 4010 to 4013 and the denial carry outcome, duration, status and exception type only; never the
    /// SSN or any history value, on success and on every failure path.
    /// </summary>
    [Category("Story:E3-C2-2")]
    [Category("Req:R-CR2-8")]
    [Test]
    public async Task Logs_record_outcome_and_duration_but_never_the_SSN_or_history_values()
    {
        var (client, _, logs, provider) = Build((request, attempt, _) => attempt switch
        {
            1 => Respond(HttpStatusCode.OK, FoundBody),
            2 => Respond(HttpStatusCode.NotFound),
            3 => Respond(HttpStatusCode.OK, """{"branch":"Coast Guard","serviceStartDate":"2010-01-01","serviceEndDate":"2009-01-01","characterOfService":"General"}"""),
            _ => throw new HttpRequestException("POST http://history.test/veterans/service-history/lookup ssn=900-12-3459 failed"),
        });
        await using var _ = provider;

        await client.LookupAsync("900-12-3456", CancellationToken.None);
        await client.LookupAsync("900123457", CancellationToken.None);
        await client.LookupAsync("900-12-3458", CancellationToken.None);
        await client.LookupAsync("900-12-3459", CancellationToken.None);
        await client.LookupAsync("bad", CancellationToken.None);

        Assert.Equal(["Found", "NotFound"], logs.WithEventId(4010).Select(r => (string?)r.GetStructuredStateValue("Outcome")));
        Assert.All(logs.WithEventId(4010), r => Assert.NotNull(r.GetStructuredStateValue("DurationMs")));
        Assert.Equal(["UnusableResponse", "HttpRequestException"], logs.WithEventId(4011).Select(r => (string?)r.GetStructuredStateValue("Reason")));
        Assert.Single(logs.WithEventId(4012));
        Assert.Equal("HttpRequestException", Assert.Single(logs.WithEventId(4013)).GetStructuredStateValue("Reason"));
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Category?.StartsWith("Polly", StringComparison.Ordinal) == true);
        LogAssertions.ContainsNoSensitiveValues(logs, "12-3456", "12-3457", "12-3458", "12-3459",
            "Army", "Honorable", "Coast Guard", "General", "2001-02-15", "2005-02-14", "2026-01-05");

        var (denied, _, deniedLogs, deniedProvider) = Build((_, _, _) => Respond(HttpStatusCode.OK, FoundBody), new TestCurrentUser());
        await using var __ = deniedProvider;
        await denied.LookupAsync("900-12-3456", CancellationToken.None);
        Assert.Single(deniedLogs.WithEventId(SecurityEvents.AuthorizationDeniedId));
        LogAssertions.ContainsNoSensitiveValues(deniedLogs, "12-3456");
    }
}
