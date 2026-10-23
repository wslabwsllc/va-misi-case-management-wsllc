using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CaseIntake.VeteranHistory.Mock;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// E4-C2-1/2 behavior of <c>POST /veterans/service-history/lookup</c>, run against the in-process mock and, inside the
/// <c>tests</c> service, against the real <c>veteran-history</c> container as well. Every response the contract
/// document lists is produced here.
/// </summary>
public class HistoryMockServiceTests : ClassFixture<HistoryMockServiceTests, HistoryMockServiceFactory>
{
    private static HistoryMockServiceFactory factory => Shared;

    private const string Route = HistoryMockProgram.LookupRoute;

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task The_documented_veteran_returns_200_with_the_Government_example_and_last_verified(string target)
    {
        using var client = HistoryTarget.CreateClient(target, factory);

        using var response = await client.PostAsJsonAsync(Route, new { ssn = VeteranDatasetGenerator.KnownFoundSsn });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["branch", "characterOfService", "lastVerifiedDate", "serviceEndDate", "serviceStartDate"],
            body.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Army", body.RootElement.GetProperty("branch").GetString());
        Assert.Equal("2001-02-15", body.RootElement.GetProperty("serviceStartDate").GetString());
        Assert.Equal("2005-02-14", body.RootElement.GetProperty("serviceEndDate").GetString());
        Assert.Equal("Honorable", body.RootElement.GetProperty("characterOfService").GetString());
        Assert.Equal("2026-01-05", body.RootElement.GetProperty("lastVerifiedDate").GetString());
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task Dashed_and_nine_digit_forms_return_the_same_body(string target)
    {
        using var client = HistoryTarget.CreateClient(target, factory);

        var dashed = await PostAsync(client, VeteranDatasetGenerator.KnownFoundSsn);
        var again = await PostAsync(client, VeteranDatasetGenerator.KnownFoundSsn);
        var undashed = await PostAsync(client, VeteranDatasetGenerator.KnownFoundSsn.Replace("-", "", StringComparison.Ordinal));

        Assert.Equal(dashed, again);
        Assert.Equal(dashed, undashed);

        static async Task<string> PostAsync(HttpClient client, string ssn)
        {
            using var response = await client.PostAsJsonAsync(Route, new { ssn });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
    }

    [Test]
    public async Task Open_service_and_unverified_records_omit_those_fields()
    {
        var open = ServiceHistoryDatasetGenerator.Generate().First(r => r.ServiceEndDate is null && r.LastVerifiedDate is not null);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, new { ssn = open.Ssn });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("serviceEndDate", out _));
        Assert.True(body.RootElement.TryGetProperty("lastVerifiedDate", out _));
    }

    [TestCase(ServiceHistoryDatasetGenerator.KnownNoRecordSsn)]
    [TestCase(VeteranDatasetGenerator.KnownAbsentSsn)]
    [TestCase("900-00-0001")]
    public async Task A_veteran_without_a_record_returns_404(string ssn)
    {
        foreach (var target in HistoryTarget.Names())
        {
            using var client = HistoryTarget.CreateClient(target, factory);

            using var response = await client.PostAsJsonAsync(Route, new { ssn });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task The_outage_veteran_returns_503_in_either_form(string target)
    {
        using var client = HistoryTarget.CreateClient(target, factory);

        foreach (var ssn in new[] { ServiceHistoryDatasetGenerator.KnownOutageSsn, ServiceHistoryDatasetGenerator.KnownOutageSsn.Replace("-", "", StringComparison.Ordinal) })
        {
            using var response = await client.PostAsJsonAsync(Route, new { ssn });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    public static TheoryData<string, string> InvalidBodies()
    {
        var data = new TheoryData<string, string>();
        foreach (var target in HistoryTarget.Names())
        {
            foreach (var body in new[] { "", "{", "not json", "{}", "{\"ssn\":null}", "{\"ssn\":900456789}", "{\"ssn\":\"\"}", "{\"ssn\":\"   \"}" })
            {
                data.Add(target, body);
            }
        }
        return data;
    }

    [TestCaseSource(nameof(InvalidBodies))]
    public async Task A_blank_missing_or_unreadable_ssn_returns_400_problem_json(string target, string body)
    {
        using var client = HistoryTarget.CreateClient(target, factory);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [TestCaseSource(typeof(HistoryTarget), nameof(HistoryTarget.All))]
    public async Task Health_returns_200(string target)
    {
        using var client = HistoryTarget.CreateClient(target, factory);

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Test]
    public async Task Every_response_the_contract_documents_is_produced_by_the_mock()
    {
        var documented = await HistoryContract.ResponseStatusesAsync();
        using var client = factory.CreateClient();
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ssn in new[] { VeteranDatasetGenerator.KnownFoundSsn, ServiceHistoryDatasetGenerator.KnownNoRecordSsn, ServiceHistoryDatasetGenerator.KnownOutageSsn, "" })
        {
            using var response = await client.PostAsJsonAsync(Route, new { ssn });
            produced.Add(((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(documented.Order(StringComparer.Ordinal), produced.Order(StringComparer.Ordinal));
    }

    /// <summary>E3-C2-2: event 4060 carries the outcome and duration only, never the SSN or the history values.</summary>
    [Test]
    public async Task Lookups_log_the_outcome_and_duration_but_never_the_SSN_or_history_values()
    {
        using var client = factory.CreateClient();
        foreach (var ssn in new[] { VeteranDatasetGenerator.KnownFoundSsn, ServiceHistoryDatasetGenerator.KnownNoRecordSsn, ServiceHistoryDatasetGenerator.KnownOutageSsn, " " })
        {
            using var _ = await client.PostAsJsonAsync(Route, new { ssn });
        }

        var served = factory.Logs.WithEventId(4060).ToList();
        foreach (var outcome in new[] { "Found", "NotFound", "Unavailable", "InvalidInput" })
        {
            var record = served.First(r => (string?)r.GetStructuredStateValue("Outcome") == outcome);
            Assert.Equal("ServiceHistoryServed", record.Id.Name);
            Assert.Equal(["DurationMs", "Outcome", "{OriginalFormat}"],record.StructuredState!.Select(kv => kv.Key).Order(StringComparer.Ordinal));
        }
        // Hyphenated fragments, not bare digits: a logged duration could match digits by chance.
        LogAssertions.ContainsNoSensitiveValues(factory.Logs, "12-3456", "17-3062", "91-6025", "Army", "Honorable", "2001-02-15", "2005-02-14");
    }
}
