using System.Text.Json;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.VeteranLookup.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// E4-S2/S3 end to end: the production client and pipeline against the real mock, over HTTP. In the <c>tests</c>
/// service this also runs against the <c>veteran-lookup</c> container (a real socket on the Compose network).
/// The tests keep the production timeouts (2 s per attempt, 5 s total); <see cref="InitializeAsync"/> first sends one
/// lookup with a relaxed budget, so first-request start-up cost (JIT, dataset load) is never charged to that budget.
/// </summary>
public class ClientAgainstMockTests(MockServiceFactory factory) : IClassFixture<MockServiceFactory>, IAsyncLifetime
{
    // Cold start under linux/amd64 emulation on Apple Silicon measured 1.5-1.9 s for the first request, against the
    // production 2 s attempt limit; 30 s leaves room for a slow or busy machine.
    private static readonly TimeSpan WarmUpBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Warms the client pipeline and each mock with one lookup with a generous budget, through the same code path the tests
    /// use. The result is not asserted; the tests below check behavior with the production budget.
    /// </summary>
    public async Task InitializeAsync()
    {
        foreach (var target in LookupTarget.Names())
        {
            await using var provider = BuildProvider(target, WarmUpBudget);
            await provider.GetRequiredService<IVeteranLookupClient>().LookupAsync(VeteranDatasetGenerator.KnownFoundSsn, CancellationToken.None);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<VeteranLookupResult> LookupAsync(string target, string ssn)
    {
        await using var provider = BuildProvider(target);
        return await provider.GetRequiredService<IVeteranLookupClient>().LookupAsync(ssn, CancellationToken.None);
    }

    /// <param name="budget">Attempt and total timeout; null keeps the production defaults.</param>
    /// <param name="recorded">When set, every request the client sends is recorded here.</param>
    private ServiceProvider BuildProvider(string target, TimeSpan? budget = null, List<RecordedRequest>? recorded = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["VeteranLookup:BaseUrl"] = target == LookupTarget.Compose ? LookupTarget.ComposeBaseUrl : "http://localhost/",
        };
        if (budget is { } timeout)
        {
            settings["VeteranLookup:AttemptTimeout"] = timeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
            settings["VeteranLookup:TotalTimeout"] = timeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ICurrentUser>(new TestCurrentUser(Permissions.VeteranLookup));
        var http = VeteranLookupModule.AddVeteranLookupHttpClient(services);
        if (target == LookupTarget.InProcess)
        {
            http.ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
        }
        if (recorded is not null)
        {
            http.AddHttpMessageHandler(() => new RecordingHandler(recorded));
        }

        return services.BuildServiceProvider();
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task The_documented_demo_SSN_is_Found_with_the_dataset_values(string target)
    {
        var result = await LookupAsync(target, VeteranDatasetGenerator.KnownFoundSsn);

        var expected = VeteranDatasetGenerator.Generate()[0];
        Assert.Equal(new VeteranLookupResult(VeteranLookupOutcome.Found, expected.Name, expected.Address), result);
    }

    // Specification 4.6: the production client's wire request matches the Government definition, checked against the
    // definition itself (method, path, content type, request schema), and the real service answers it.
    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task The_client_sends_the_request_the_spec_defines(string target)
    {
        var spec = await GovernmentSpec.GetAsync();
        var recorded = new List<RecordedRequest>();
        await using var provider = BuildProvider(target, recorded: recorded);

        var result = await provider.GetRequiredService<IVeteranLookupClient>().LookupAsync(VeteranDatasetGenerator.KnownFoundSsn, CancellationToken.None);

        Assert.Equal(VeteranLookupOutcome.Found, result.Outcome);
        var request = Assert.Single(recorded);
        Assert.Equal(spec.Method, request.Method);
        Assert.Equal(spec.Path, request.Uri!.AbsolutePath);
        Assert.Equal(spec.RequestContentType, request.ContentType);
        using var body = JsonDocument.Parse(request.Body);
        GovernmentSpec.AssertConforms(body.RootElement, spec.RequestSchema);
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task The_documented_absent_SSN_is_NotFound(string target)
    {
        Assert.Equal(VeteranLookupOutcome.NotFound, (await LookupAsync(target, VeteranDatasetGenerator.KnownAbsentSsn)).Outcome);
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task Every_dataset_record_is_found_by_the_client_in_the_nine_digit_form(string target)
    {
        await using var provider = BuildProvider(target);
        var client = provider.GetRequiredService<IVeteranLookupClient>();
        foreach (var record in VeteranDatasetGenerator.Generate())
        {
            var result = await client.LookupAsync(record.Ssn.Replace("-", "", StringComparison.Ordinal), CancellationToken.None);
            Assert.Equal(VeteranLookupOutcome.Found, result.Outcome);
            Assert.Equal(record.Name, result.Name);
        }
    }
}
