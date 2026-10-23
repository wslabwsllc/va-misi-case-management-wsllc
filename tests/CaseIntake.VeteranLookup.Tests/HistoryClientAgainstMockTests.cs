using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using CaseIntake.VeteranLookup.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// E4-C2-3 end to end: the production Service History client and pipeline against the real mock, over HTTP, for each
/// demo SSN of plan 0.4. In the <c>tests</c> service this also runs against the <c>veteran-history</c> container. As in
/// <see cref="ClientAgainstMockTests"/>, one warm-up lookup with a relaxed budget runs first.
/// </summary>
public class HistoryClientAgainstMockTests : ClassFixture<HistoryClientAgainstMockTests, HistoryMockServiceFactory>
{
    private static HistoryMockServiceFactory factory => Shared;

    private static readonly TimeSpan WarmUpBudget = TimeSpan.FromSeconds(30);

    [SetUp]
    public async Task WarmUpAsync()
    {
        foreach (var target in HistoryTarget.Names())
        {
            await using var provider = BuildProvider(target, WarmUpBudget);
            await provider.GetRequiredService<IVeteranServiceHistoryClient>().LookupAsync(VeteranDatasetGenerator.KnownFoundSsn, CancellationToken.None);
        }
    }

    private static ServiceProvider BuildProvider(string target, TimeSpan? budget = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["VeteranHistory:BaseUrl"] = target == LookupTarget.Compose ? HistoryTarget.ComposeBaseUrl : "http://localhost/",
            // The 503 path retries once; a short delay keeps the test quick without changing the behavior.
            ["VeteranHistory:RetryDelay"] = TimeSpan.FromMilliseconds(10).ToString("c", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (budget is { } timeout)
        {
            settings["VeteranHistory:AttemptTimeout"] = timeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
            settings["VeteranHistory:TotalTimeout"] = timeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ICurrentUser>(new TestCurrentUser(Permissions.VeteranLookup));
        var http = VeteranLookupModule.AddServiceHistoryHttpClient(services);
        if (target == LookupTarget.InProcess)
        {
            http.ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
        }
        return services.BuildServiceProvider();
    }

    private static async Task<ServiceHistoryResult> LookupAsync(string target, string ssn)
    {
        await using var provider = BuildProvider(target);
        return await provider.GetRequiredService<IVeteranServiceHistoryClient>().LookupAsync(ssn, CancellationToken.None);
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task The_documented_veteran_is_Found_with_the_Government_example(string target)
    {
        var expected = ServiceHistoryDatasetGenerator.GovernmentExample;

        var result = await LookupAsync(target, VeteranDatasetGenerator.KnownFoundSsn);

        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.Found, new ServiceHistory(expected.Branch, expected.ServiceStartDate,
            expected.ServiceEndDate, expected.CharacterOfService, expected.LastVerifiedDate)), result);
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task The_no_record_veteran_is_NotFound(string target)
    {
        Assert.Equal(VeteranLookupOutcome.NotFound, (await LookupAsync(target, ServiceHistoryDatasetGenerator.KnownNoRecordSsn)).Outcome);
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task The_outage_veteran_is_Unavailable(string target)
    {
        Assert.Equal(VeteranLookupOutcome.Unavailable, (await LookupAsync(target, ServiceHistoryDatasetGenerator.KnownOutageSsn)).Outcome);
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task The_absent_SSN_is_NotFound(string target)
    {
        Assert.Equal(VeteranLookupOutcome.NotFound, (await LookupAsync(target, VeteranDatasetGenerator.KnownAbsentSsn)).Outcome);
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task Every_dataset_record_is_Found_in_the_nine_digit_form(string target)
    {
        await using var provider = BuildProvider(target);
        var client = provider.GetRequiredService<IVeteranServiceHistoryClient>();
        foreach (var record in ServiceHistoryDatasetGenerator.Generate())
        {
            var result = await client.LookupAsync(record.Ssn.Replace("-", "", StringComparison.Ordinal), CancellationToken.None);
            Assert.Equal(VeteranLookupOutcome.Found, result.Outcome);
            Assert.Equal(record.Branch, result.History!.Branch);
            Assert.Equal(record.ServiceEndDate, result.History.ServiceEndDate);
        }
    }
}
