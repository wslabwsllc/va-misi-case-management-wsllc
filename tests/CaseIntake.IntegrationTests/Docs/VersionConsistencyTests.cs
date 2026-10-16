using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CaseIntake.IntegrationTests.Docs;

/// <summary>Keeps docs/versions.md, global.json, Directory.Packages.props and the banned-API setup from drifting.</summary>
public class VersionConsistencyTests
{
    private static string VersionsDoc => File.ReadAllText(RepositoryRoot.File("docs", "versions.md"));

    [Fact]
    public void Every_runtime_package_pin_is_listed_in_versions_md_with_the_same_version()
    {
        var props = XDocument.Load(RepositoryRoot.File("Directory.Packages.props"));
        var pins = props.Descendants("PackageVersion")
            .Select(e => (Id: (string)e.Attribute("Include")!, Version: (string)e.Attribute("Version")!)).ToList();
        Assert.NotEmpty(pins);

        foreach (var (id, version) in pins)
        {
            var row = Regex.Match(VersionsDoc, $@"\|\s*`{Regex.Escape(id)}`\s*\|\s*([^|\s]+)\s*\|");
            Assert.True(row.Success, $"docs/versions.md has no row for {id}");
            Assert.Equal(version, row.Groups[1].Value);
        }
    }

    [Fact]
    public void Sdk_version_matches_global_json_and_is_pinned_exactly()
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryRoot.File("global.json")));
        var sdk = json.RootElement.GetProperty("sdk");
        var version = sdk.GetProperty("version").GetString()!;

        Assert.Matches(@"^10\.0\.\d+$", version);
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        Assert.Contains($"| .NET SDK | {version} |", VersionsDoc);
    }

    [Fact]
    public void Dockerfile_uses_the_pinned_sdk_version_and_a_pinned_dotnet_ef_tool_matches_EF_Core()
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryRoot.File("global.json")));
        var sdk = json.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        Assert.Contains($"dotnet/sdk:{sdk}-noble", File.ReadAllText(RepositoryRoot.File("Dockerfile")));

        var tools = File.ReadAllText(RepositoryRoot.File("dotnet-tools.json"));
        var ef = XDocument.Load(RepositoryRoot.File("Directory.Packages.props")).Descendants("PackageVersion")
            .Single(e => (string)e.Attribute("Include")! == "Microsoft.EntityFrameworkCore").Attribute("Version")!.Value;
        Assert.Contains($"\"version\": \"{ef}\"", tools);
    }

    [Fact]
    public void Banned_api_analyzer_is_wired_into_every_project()
    {
        var props = File.ReadAllText(RepositoryRoot.File("Directory.Build.props"));
        var banned = File.ReadAllText(RepositoryRoot.File("BannedSymbols.txt"));

        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", props);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", props);
        foreach (var symbol in new[] { "P:System.DateTime.Now", "P:System.DateTime.Today", "P:System.DateTimeOffset.Now" })
        {
            Assert.Contains(symbol, banned);
        }
    }

    [Fact]
    public void Every_project_targets_net10_through_Directory_Build_props_only()
    {
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", File.ReadAllText(RepositoryRoot.File("Directory.Build.props")));
        foreach (var csproj in Directory.EnumerateFiles(RepositoryRoot.File("src"), "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(RepositoryRoot.File("tests"), "*.csproj", SearchOption.AllDirectories)))
        {
            Assert.DoesNotContain("<TargetFramework>", File.ReadAllText(csproj));
        }
    }
}
