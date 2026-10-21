using System.Net;
using System.Text;
using System.Text.Json;
using CaseIntake.VeteranLookup.Mock;
using Microsoft.OpenApi;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// US-4.1 / R12 (E4-S2). Specification 4.6 requires the lookup service to conform to the Government's definition. The
/// mock serves that definition verbatim as its contract, and these tests prove that the implementation conforms to it:
/// the document generated from the mock's endpoint declarations may differ from the definition only by the documented
/// extensions in <see cref="Allowlist"/> (assumptions D11-D15, ADR 0008).
/// </summary>
public class OpenApiConformanceTests : ClassFixture<OpenApiConformanceTests, MockServiceFactory>
{
    private static MockServiceFactory factory => Shared;

    private static readonly string Operation = $"/paths/{Escape(MockProgram.LookupRoute)}/post";

    /// <summary>
    /// Elements the generated document may add to the definition. An entry covers that leaf and everything below it.
    /// Each one is a documented extension; anything else the generated document adds fails the conformance test.
    /// </summary>
    private static readonly (string Path, string Reason)[] Allowlist =
    [
        ($"{Operation}/responses/400", "D11: 400 problem+json for request bodies the definition does not define"),
        ($"{Operation}/responses/415", "D12: 415 for a non-JSON content type"),
        ($"{Operation}/operationId", "D13: generator metadata"),
        ($"{Operation}/tags", "D13: generator metadata"),
        ("/tags", "D13: generator metadata"),
        ($"{Operation}/description", "D13: describes the 400/415 extensions"),
        ("/components/schemas/VeteranLookupRequest", "D13: $ref target of the request schema (compared inline)"),
        ("/components/schemas/VeteranLookupResponse", "D13: $ref target of the 200 schema (compared inline)"),
        ("/components/schemas/ProblemDetails", "D13: $ref target of the 400 schema"),
        ($"{Operation}/responses/200/content/application~1json/schema/required{{name}}", "D14: the mock always returns name"),
        ($"{Operation}/responses/200/content/application~1json/schema/required{{address}}", "D14: the mock always returns address"),
    ];

    [Test]
    public void The_Government_contract_file_is_unchanged()
    {
        Assert.True(GovernmentSpec.Sha256(GovernmentSpec.RawBytes()) == GovernmentSpec.PinnedSha256,
            "docs/contracts/veteran-lookup.spec.openapi.yaml is the Government's definition (specification 4.6) and must stay " +
            "byte for byte as supplied. Do not reformat or edit it; restore it from the specification.");
    }

    [Test]
    public async Task The_Government_contract_parses_as_OpenAPI_3_0_3()
    {
        var result = await GovernmentSpec.ReadAsync();

        Assert.Empty(result.Diagnostic?.Errors ?? []);
        Assert.Equal(OpenApiSpecVersion.OpenApi3_0, result.Diagnostic?.SpecificationVersion);
        Assert.StartsWith("openapi: 3.0.3\n", Encoding.UTF8.GetString(GovernmentSpec.RawBytes()), StringComparison.Ordinal);
    }

    [TestCaseSource(typeof(LookupTarget), nameof(LookupTarget.All))]
    public async Task The_mock_serves_the_Government_contract_byte_for_byte(string target)
    {
        using var client = LookupTarget.CreateClient(target, factory);

        using var response = await client.GetAsync(GovernmentContract.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GovernmentContract.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(GovernmentSpec.PinnedSha256, GovernmentSpec.Sha256(await response.Content.ReadAsByteArrayAsync()));
    }

    // Run against the Compose container as well: it is the Release/Production build the evaluators run.
    [TestCaseSource(typeof(LookupTarget), nameof(LookupTarget.All))]
    public async Task The_implementation_conforms_to_the_Government_contract(string target)
    {
        var spec = Flatten((await GovernmentSpec.GetAsync()).Root);
        using var generatedDocument = await GetGeneratedDocumentAsync(target);
        var generated = Flatten(generatedDocument.RootElement);

        var problems = new List<string>();
        foreach (var (path, value) in spec)
        {
            if (!generated.TryGetValue(path, out var actual))
            {
                problems.Add($"missing  {path} = {value}");
            }
            else if (actual != value)
            {
                problems.Add($"differs  {path}: definition {value}, implementation {actual}");
            }
        }
        problems.AddRange(generated.Keys.Where(path => !spec.ContainsKey(path) && !IsAllowlisted(path)).Select(path => $"extra    {path}"));

        Assert.True(problems.Count == 0,
            "The implementation does not conform to the Government definition (specification 4.6):\n" + string.Join("\n", problems));
    }

    [TestCaseSource(typeof(LookupTarget), nameof(LookupTarget.All))]
    public async Task The_implementation_document_is_OpenAPI_3_0_and_host_independent(string target)
    {
        using var generated = await GetGeneratedDocumentAsync(target);

        Assert.Matches(@"^3\.0\.\d+$", generated.RootElement.GetProperty("openapi").GetString());   // D15: any 3.0.x
        Assert.False(generated.RootElement.TryGetProperty("servers", out _));
    }

    [Test]
    public async Task Every_allowlist_entry_matches_the_generated_document()
    {
        using var generatedDocument = await GetGeneratedDocumentAsync(LookupTarget.InProcess);
        var generated = Flatten(generatedDocument.RootElement);

        var stale = Allowlist.Where(entry => !generated.Keys.Any(path => Covers(entry.Path, path))).Select(e => e.Path).ToList();

        Assert.True(stale.Count == 0, "Allowlist entries no longer needed (remove them and their assumption rows):\n" + string.Join("\n", stale));
    }

    private async Task<JsonDocument> GetGeneratedDocumentAsync(string target)
    {
        using var client = LookupTarget.CreateClient(target, factory);
        return JsonDocument.Parse(await client.GetStringAsync($"/openapi/{MockProgram.ImplementationDocumentName}.json"));
    }

    private static bool IsAllowlisted(string path) => Allowlist.Any(entry => Covers(entry.Path, path));

    private static bool Covers(string entry, string path) =>
        path == entry || path.StartsWith(entry + "/", StringComparison.Ordinal) || path.StartsWith(entry + "{", StringComparison.Ordinal);

    /// <summary>
    /// Every scalar of an OpenAPI document by JSON Pointer path. Local <c>$ref</c>s are followed, so a schema compares the
    /// same inline or by reference; <c>required</c> arrays become one <c>required{name}</c> entry per name (order is not
    /// significant). Left out on both sides: the <c>openapi</c> version (the definition is 3.0.3, the generator writes its
    /// own 3.0.x, D15) and <c>example</c> values (illustrative, not part of the shape; the definition's SSN example is not
    /// reproduced). A schema property that happens to be named <c>example</c> is still compared.
    /// </summary>
    private static Dictionary<string, string> Flatten(JsonElement root)
    {
        var leaves = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject().Where(p => p.Name != "openapi"))
        {
            Walk(root, property.Value, "/" + Escape(property.Name), leaves);
        }
        return leaves;
    }

    private static void Walk(JsonElement root, JsonElement element, string path, Dictionary<string, string> leaves)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when element.TryGetProperty("$ref", out var reference):
                Walk(root, Resolve(root, reference.GetString()!), path, leaves);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "example" && !path.EndsWith("/properties", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var child = path + "/" + Escape(property.Name);
                    if (property.Name == "required" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var name in property.Value.EnumerateArray())
                        {
                            leaves[$"{child}{{{name.GetString()}}}"] = "true";
                        }
                    }
                    else
                    {
                        Walk(root, property.Value, child, leaves);
                    }
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(root, item, $"{path}/{index++}", leaves);
                }
                break;
            default:
                leaves[path] = element.GetRawText();
                break;
        }
    }

    /// <summary>Follows a local <c>$ref</c> such as <c>#/components/schemas/VeteranLookupRequest</c>.</summary>
    private static JsonElement Resolve(JsonElement root, string reference)
    {
        var current = root;
        foreach (var segment in reference.TrimStart('#', '/').Split('/'))
        {
            current = current.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
        }
        return current;
    }

    private static string Escape(string name) =>
        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
