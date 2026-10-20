using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// E4-S1 behavior of <c>POST /veterans/lookup</c>, run against the in-process mock and, inside the <c>tests</c>
/// service, against the real <c>veteran-lookup</c> container as well.
/// </summary>
public class MockServiceTests(MockServiceFactory factory) : IClassFixture<MockServiceFactory>
{
    private const string Route = "/veterans/lookup";

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task A_known_SSN_returns_200_with_exactly_name_and_address(string target)
    {
        using var client = LookupTarget.CreateClient(target, factory);

        using var response = await client.PostAsJsonAsync(Route, new { ssn = VeteranDatasetGenerator.KnownFoundSsn });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["address", "name"], body.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("address").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("name").GetString()));
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task The_same_SSN_returns_the_same_body_every_time_in_either_accepted_form(string target)
    {
        using var client = LookupTarget.CreateClient(target, factory);

        var first = await PostAsync(client, VeteranDatasetGenerator.KnownFoundSsn);
        var second = await PostAsync(client, VeteranDatasetGenerator.KnownFoundSsn);
        var undashed = await PostAsync(client, VeteranDatasetGenerator.KnownFoundSsn.Replace("-", "", StringComparison.Ordinal));

        Assert.Equal(first, second);
        Assert.Equal(first, undashed);

        static async Task<string> PostAsync(HttpClient client, string ssn)
        {
            using var response = await client.PostAsJsonAsync(Route, new { ssn });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task An_unknown_SSN_returns_404(string target)
    {
        using var client = LookupTarget.CreateClient(target, factory);

        using var response = await client.PostAsJsonAsync(Route, new { ssn = VeteranDatasetGenerator.KnownAbsentSsn });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Specification 4.6: the service conforms to the Government definition. These requests are built only from the
    // definition (path, method, content type, required property) and the responses are checked against its schemas.
    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task A_request_shaped_by_the_spec_for_a_known_SSN_gets_the_spec_200_shape(string target)
    {
        var spec = await GovernmentSpec.GetAsync();
        using var client = LookupTarget.CreateClient(target, factory);

        using var request = spec.CreateLookupRequest(VeteranDatasetGenerator.KnownFoundSsn);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("200", spec.ResponseStatuses);
        Assert.Equal(spec.ResponseContentType("200"), response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        GovernmentSpec.AssertConforms(body.RootElement, spec.ResponseSchema("200"));
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task A_request_shaped_by_the_spec_for_an_unknown_SSN_gets_the_spec_404(string target)
    {
        var spec = await GovernmentSpec.GetAsync();
        using var client = LookupTarget.CreateClient(target, factory);

        using var request = spec.CreateLookupRequest(VeteranDatasetGenerator.KnownAbsentSsn);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("404", spec.ResponseStatuses);
    }

    public static TheoryData<string, string> InvalidBodies()
    {
        var data = new TheoryData<string, string>();
        foreach (var target in LookupTarget.Names())
        {
            foreach (var body in new[] { "", "{", "not json", "{}", "{\"ssn\":null}", "{\"ssn\":900456789}", "{\"ssn\":\"\"}", "{\"ssn\":\"   \"}", "{\"other\":\"x\"}" })
            {
                data.Add(target, body);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Absent_or_malformed_JSON_and_a_missing_null_non_string_or_blank_ssn_return_400(string target, string body)
    {
        using var client = LookupTarget.CreateClient(target, factory);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task A_non_JSON_content_type_returns_415(string target)
    {
        using var client = LookupTarget.CreateClient(target, factory);
        using var content = new StringContent(VeteranDatasetGenerator.KnownFoundSsn, Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(Route, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(LookupTarget.All), MemberType = typeof(LookupTarget))]
    public async Task Health_returns_200(string target)
    {
        using var client = LookupTarget.CreateClient(target, factory);

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Lookups_log_the_outcome_but_never_the_SSN_or_veteran_details()
    {
        using var client = factory.CreateClient();
        using var found = await client.PostAsJsonAsync(Route, new { ssn = VeteranDatasetGenerator.KnownFoundSsn });
        var veteran = await found.Content.ReadFromJsonAsync<JsonElement>();
        using var missing = await client.PostAsJsonAsync(Route, new { ssn = VeteranDatasetGenerator.KnownAbsentSsn });

        Assert.Contains(factory.Logs.WithEventId(4050), r => (string?)r.GetStructuredStateValue("Outcome") == "Found");
        Assert.Contains(factory.Logs.WithEventId(4050), r => (string?)r.GetStructuredStateValue("Outcome") == "NotFound");
        LogAssertions.ContainsNoSensitiveValues(factory.Logs,
            veteran.GetProperty("name").GetString()!, veteran.GetProperty("address").GetString()!, "12-3456");   // 9-digit form: ContainsSsn
    }
}
