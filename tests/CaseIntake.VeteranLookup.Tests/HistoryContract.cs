using System.Text.Json;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// The team's Service History contract (E4-C2-2), read with Microsoft.OpenApi's YAML reader. Unlike
/// <see cref="GovernmentSpec"/> it is not pinned: it is the team's own document and may change with the mock.
/// </summary>
internal static class HistoryContract
{
    public static readonly string[] RepositoryPath = ["docs", "contracts", "veteran-service-history.openapi.yaml"];

    /// <summary>The documented response statuses of the single operation, e.g. "200", "404".</summary>
    public static async Task<IReadOnlyList<string>> ResponseStatusesAsync()
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();
        await using var stream = File.OpenRead(RepositoryRoot.File(RepositoryPath));
        var result = await OpenApiDocument.LoadAsync(stream, OpenApiConstants.Yaml, settings);
        Assert.Empty(result.Diagnostic?.Errors ?? []);
        var json = await result.Document!.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0);
        using var document = JsonDocument.Parse(json);
        var operation = document.RootElement.GetProperty("paths").GetProperty("/veterans/service-history/lookup").GetProperty("post");
        return [.. operation.GetProperty("responses").EnumerateObject().Select(p => p.Name)];
    }
}
