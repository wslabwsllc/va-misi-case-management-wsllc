using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CaseIntake.IntegrationTests.Docs;

/// <summary>Keeps docs/versions.md, global.json, Directory.Packages.props and the banned-API setup from drifting.</summary>
public partial class VersionConsistencyTests
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

    // FROM [--platform=...] <image> in Dockerfiles, and image: <image> in Compose. Stage names have no ':' and are skipped.
    [GeneratedRegex(@"^\s*(?:FROM\s+(?:--platform=\S+\s+)?|image:\s*)(?<image>\S+:\S+)", RegexOptions.Multiline)]
    private static partial Regex ImageReference();

    private static List<string> ContainerImages() =>
    [
        .. Directory.EnumerateFiles(RepositoryRoot.Path, "Dockerfile", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(d => d is "bin" or "obj" or ".git" or "node_modules"))
            .Append(RepositoryRoot.File("docker-compose.yml"))
            .SelectMany(f => ImageReference().Matches(File.ReadAllText(f)).Select(m => m.Groups["image"].Value))
            .Where(image => !image.StartsWith("caseintake-", StringComparison.Ordinal))   // built locally from this repository
            .Distinct(),
    ];

    [Fact]
    public void Every_container_image_used_by_a_Dockerfile_or_Compose_is_listed_in_versions_md()
    {
        var images = ContainerImages();
        Assert.Contains(images, i => i.StartsWith("mcr.microsoft.com/dotnet/aspnet:", StringComparison.Ordinal));
        Assert.Contains(images, i => i.StartsWith("postgres:", StringComparison.Ordinal));

        foreach (var image in images)
        {
            Assert.True(VersionsDoc.Contains($"`{image}`", StringComparison.Ordinal), $"docs/versions.md does not list the image {image}");
        }
    }

    [Fact]
    public void Runtime_image_tag_matches_the_runtime_versions_in_versions_md()
    {
        var tags = ContainerImages().Where(i => i.StartsWith("mcr.microsoft.com/dotnet/aspnet:", StringComparison.Ordinal))
            .Select(i => i.Split(':')[1].Split('-')[0]).Distinct().ToList();
        var runtime = Assert.Single(tags);   // one runtime version across the app and the lookup mock

        foreach (var framework in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
        {
            var row = Regex.Match(VersionsDoc, $@"\(`{Regex.Escape(framework)}`\)\s*\|\s*([^|\s]+)\s*\|");
            Assert.True(row.Success, $"docs/versions.md has no row for {framework}");
            Assert.Equal(runtime, row.Groups[1].Value);
        }
    }
}
