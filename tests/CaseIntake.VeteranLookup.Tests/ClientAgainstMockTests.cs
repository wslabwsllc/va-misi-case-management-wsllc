using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.VeteranLookup.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// E4-S2/S3 end to end: the production client and pipeline against the real mock, over HTTP. In the <c>tests</c>
/// service this also runs against the <c>veteran-lookup</c> container (a real socket on the Compose network).
/// </summary>
public class ClientAgainstMockTests(MockServiceFactory factory) : IClassFixture<MockServiceFactory>
{
    private async Task<VeteranLookupResult> LookupAsync(string target, string ssn)
    {
        await using var provider = BuildProvider(target);
        return await provider.GetRequiredService<IVeteranLookupClient>().LookupAsync(ssn, CancellationToken.None);
    }

    private ServiceProvider BuildProvider(string target)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VeteranLookup:BaseUrl"] = target == LookupTarget.Compose ? LookupTarget.ComposeBaseUrl : "http://localhost/",
        }).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ICurrentUser>(new TestCurrentUser(Permissions.VeteranLookup));
        var http = VeteranLookupModule.AddVeteranLookupHttpClient(services);
        if (target == LookupTarget.InProcess)
        {
            http.ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
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
