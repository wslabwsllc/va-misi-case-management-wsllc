using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// The Government's Veteran Lookup definition (specification 4.6), read from the committed verbatim file. Tests take the
/// path, method, content types and schemas from here rather than from literals, so they check conformance to the
/// definition itself. The file is pinned by SHA-256 because it must never be edited ("tidied") by the team.
/// </summary>
internal static class GovernmentSpec
{
    public static readonly string[] RepositoryPath = ["docs", "contracts", "veteran-lookup.spec.openapi.yaml"];

    /// <summary>SHA-256 of the file as supplied (UTF-8, LF line endings, one trailing newline).</summary>
    public const string PinnedSha256 = "bbd95fc2a187220e9bfc1860c84fbf148bfc8b10282ae82f13c05bbb84730f26";

    private static readonly Lazy<Task<View>> Parsed = new(ParseAsync);

    public static byte[] RawBytes() => File.ReadAllBytes(RepositoryRoot.File(RepositoryPath));

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>The definition read with Microsoft.OpenApi's YAML reader, with its diagnostics.</summary>
    public static async Task<ReadResult> ReadAsync()
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();
        using var stream = new MemoryStream(RawBytes());
        return await OpenApiDocument.LoadAsync(stream, OpenApiConstants.Yaml, settings);
    }

    /// <summary>The parsed definition as JSON, plus the single lookup operation's shape.</summary>
    public static Task<View> GetAsync() => Parsed.Value;

    private static async Task<View> ParseAsync()
    {
        var result = await ReadAsync();
        Assert.Empty(result.Diagnostic?.Errors ?? []);
        var json = await result.Document!.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0);
        using var document = JsonDocument.Parse(json);
        return new View(document.RootElement.Clone());
    }

    /// <summary>
    /// Asserts that a JSON value matches a schema from the definition: the declared type, required properties present,
    /// and (stricter than OpenAPI's default) no properties the schema does not declare. Only the keywords the definition
    /// uses are supported; anything else fails so the check is never silently partial.
    /// </summary>
    public static void AssertConforms(JsonElement value, JsonElement schema, string at = "$")
    {
        var type = schema.GetProperty("type").GetString();
        switch (type)
        {
            case "object":
                Assert.True(value.ValueKind == JsonValueKind.Object, $"{at} must be an object");
                var properties = schema.GetProperty("properties");
                if (schema.TryGetProperty("required", out var required))
                {
                    foreach (var name in required.EnumerateArray().Select(e => e.GetString()!))
                    {
                        Assert.True(value.TryGetProperty(name, out _), $"{at}.{name} is required by the definition");
                    }
                }
                foreach (var property in value.EnumerateObject())
                {
                    Assert.True(properties.TryGetProperty(property.Name, out var propertySchema), $"{at}.{property.Name} is not declared by the definition");
                    AssertConforms(property.Value, propertySchema, $"{at}.{property.Name}");
                }
                break;
            case "string":
                Assert.True(value.ValueKind == JsonValueKind.String, $"{at} must be a string");
                break;
            default:
                Assert.Fail($"Schema type '{type}' at {at} is not supported by this check; extend it deliberately.");
                break;
        }
    }

    /// <summary>The definition's single operation, read from its JSON form.</summary>
    internal sealed class View(JsonElement root)
    {
        public JsonElement Root { get; } = root;

        public string Path => Root.GetProperty("paths").EnumerateObject().Single().Name;

        private JsonProperty OperationProperty => Root.GetProperty("paths").GetProperty(Path).EnumerateObject().Single();

        public HttpMethod Method => new(OperationProperty.Name.ToUpperInvariant());

        private JsonProperty RequestContent => OperationProperty.Value.GetProperty("requestBody").GetProperty("content").EnumerateObject().Single();

        public string RequestContentType => RequestContent.Name;

        public JsonElement RequestSchema => RequestContent.Value.GetProperty("schema");

        public IReadOnlyList<string> ResponseStatuses =>
            [.. OperationProperty.Value.GetProperty("responses").EnumerateObject().Select(p => p.Name)];

        private JsonProperty ResponseContent(string status) =>
            OperationProperty.Value.GetProperty("responses").GetProperty(status).GetProperty("content").EnumerateObject().Single();

        public string ResponseContentType(string status) => ResponseContent(status).Name;

        public JsonElement ResponseSchema(string status) => ResponseContent(status).Value.GetProperty("schema");

        /// <summary>A request built only from the definition: its method, path and content type, and a body holding just
        /// the required <c>ssn</c> property.</summary>
        public HttpRequestMessage CreateLookupRequest(string ssn)
        {
            var required = Assert.Single(RequestSchema.GetProperty("required").EnumerateArray()).GetString()!;
            var body = JsonSerializer.Serialize(new Dictionary<string, string> { [required] = ssn });
            return new HttpRequestMessage(Method, Path)
            {
                Content = new StringContent(body, Encoding.UTF8, RequestContentType),
            };
        }
    }
}
