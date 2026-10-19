using System.Net;
using System.Text.Json;
using CaseIntake.VeteranLookup.Mock;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// E4-S2: the contract is generated from the mock's code (ADR 0008). The committed YAML must equal the generated
/// document, and the generated document must describe the agreed shape: <c>POST /veterans/lookup</c>, a JSON body
/// with a required string <c>ssn</c>, 200 with required string <c>name</c> and <c>address</c>, 404, 400 and 415.
/// </summary>
public class OpenApiContractTests(MockServiceFactory factory) : IClassFixture<MockServiceFactory>
{
    private static readonly string[] SnapshotPath = ["docs", "contracts", "veteran-lookup.openapi.yaml"];

    [Fact]
    [Trait("Category", Snapshots.Category)]
    public async Task The_committed_contract_equals_the_document_generated_from_the_mock()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/openapi/{MockProgram.DocumentName}.yaml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Snapshots.Verify(await response.Content.ReadAsStringAsync(), SnapshotPath);
    }

    [Fact]
    public async Task The_generated_document_is_host_independent()
    {
        using var root = await GetDocumentAsync();

        Assert.False(root.RootElement.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0);
        Assert.StartsWith("3.1", root.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_lookup_operation_is_published()
    {
        using var root = await GetDocumentAsync();
        var paths = root.RootElement.GetProperty("paths");

        Assert.Equal([MockProgram.LookupRoute], paths.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["post"], paths.GetProperty(MockProgram.LookupRoute).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task The_request_body_is_JSON_with_a_required_string_ssn()
    {
        using var root = await GetDocumentAsync();
        var body = Operation(root).GetProperty("requestBody");

        Assert.True(body.GetProperty("required").GetBoolean());
        var schema = Resolve(root, body.GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.Contains("ssn", schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("ssn").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_200_response_has_required_string_name_and_address()
    {
        using var root = await GetDocumentAsync();
        var ok = Operation(root).GetProperty("responses").GetProperty("200");

        var schema = Resolve(root, ok.GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.Equal(["address", "name"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Order());
        foreach (var property in new[] { "name", "address" })
        {
            Assert.Equal("string", schema.GetProperty("properties").GetProperty(property).GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task The_404_400_and_415_responses_are_documented()
    {
        using var root = await GetDocumentAsync();
        var responses = Operation(root).GetProperty("responses");

        Assert.Equal(["200", "400", "404", "415"], responses.EnumerateObject().Select(p => p.Name).Order());
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        using var client = factory.CreateClient();
        return JsonDocument.Parse(await client.GetStringAsync($"/openapi/{MockProgram.DocumentName}.json"));
    }

    private static JsonElement Operation(JsonDocument root) =>
        root.RootElement.GetProperty("paths").GetProperty(MockProgram.LookupRoute).GetProperty("post");

    /// <summary>Follows a local <c>$ref</c> such as <c>#/components/schemas/VeteranLookupRequest</c>.</summary>
    private static JsonElement Resolve(JsonDocument root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var current = root.RootElement;
        foreach (var segment in reference.GetString()!.TrimStart('#', '/').Split('/'))
        {
            current = current.GetProperty(segment);
        }
        return current;
    }
}
