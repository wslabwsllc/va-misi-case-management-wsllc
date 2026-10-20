using System.Text.RegularExpressions;
using System.Xml.Linq;
using CaseIntake.TestKit;

namespace CaseIntake.IntegrationTests.Docs;

/// <summary>
/// Keeps docs/versions.md, global.json, Directory.Packages.props, the lock files, the image digests, the apt snapshot and
/// the banned-API setup from drifting, so a later rebuild cannot pick up a new upstream release.
/// </summary>
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
    public void Every_container_image_is_pinned_by_tag_and_digest_and_listed_in_versions_md()
    {
        var images = Dockerfiles.SelectMany(BaseImages)
            .Concat(Regex.Matches(File.ReadAllText(RepositoryRoot.File("docker-compose.yml")), @"^\s*image:\s*(\S+)", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .Where(image => !image.StartsWith("caseintake-", StringComparison.Ordinal)))   // built locally from the Dockerfile
            .Distinct().ToList();
        Assert.NotEmpty(images);

        foreach (var image in images)
        {
            Assert.Matches(@"^[^@\s]+:[^@\s]+@sha256:[0-9a-f]{64}$", image);
            Assert.Contains($"`{image}`", VersionsDoc);
        }
    }

    [Fact]
    public void Testcontainers_uses_the_same_postgres_image_as_compose()
    {
        Assert.Contains($"image: {PostgresDatabaseFixture.PostgresImage}", File.ReadAllText(RepositoryRoot.File("docker-compose.yml")));
    }

    [Fact]
    public void Every_project_commits_a_lock_file_and_docker_restores_in_locked_mode()
    {
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", File.ReadAllText(RepositoryRoot.File("Directory.Build.props")));
        foreach (var csproj in ProjectFiles())
        {
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(csproj)!, "packages.lock.json")), $"{csproj} has no packages.lock.json");
        }

        var restores = Dockerfiles.SelectMany(Instructions).Where(i => i.Contains("dotnet restore", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(restores);
        Assert.All(restores, restore => Assert.Contains("--locked-mode", restore));
    }

    /// <summary>
    /// The dated apt snapshot is opt-in (#26: snapshot.ubuntu.com returned 502/503 for hours and broke every uncached
    /// build). By default apt uses the live mirror with retries; passing APT_SNAPSHOT pins every OS package install, and
    /// the timestamp documented for that is the same in the Dockerfile and versions.md.
    /// </summary>
    [Fact]
    public void Apt_snapshot_is_opt_in_and_applied_to_every_OS_package_install_when_set()
    {
        var dockerfile = File.ReadAllText(RepositoryRoot.File("Dockerfile"));
        Assert.Matches(new Regex(@"^ARG APT_SNAPSHOT=$", RegexOptions.Multiline), dockerfile);   // empty default: live mirror

        var documented = Regex.Matches(dockerfile + "\n" + VersionsDoc, @"APT_SNAPSHOT=(\d{8}T\d{6}Z)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Single(documented);   // the opt-in timestamp shown in the Dockerfile matches versions.md
        Assert.Contains("APT_SNAPSHOT", VersionsDoc);

        // Every step that installs OS packages (apt directly, or Playwright's --with-deps) retries the mirror and applies
        // the snapshot when one is passed.
        var installs = Dockerfiles.SelectMany(Instructions)
            .Where(i => i.Contains("apt-get", StringComparison.Ordinal) || i.Contains("--with-deps", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(installs);
        Assert.All(installs, install =>
        {
            Assert.Contains("Acquire::Retries", install);
            Assert.Contains("if [ -n \"${APT_SNAPSHOT}\" ]", install);
            Assert.Contains("APT::Snapshot", install);
        });
    }

    private static IEnumerable<string> Dockerfiles =>
        [RepositoryRoot.File("Dockerfile"), RepositoryRoot.File("src", "CaseIntake.VeteranLookup.Mock", "Dockerfile")];

    private static IEnumerable<string> ProjectFiles() =>
        Directory.EnumerateFiles(RepositoryRoot.File("src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(RepositoryRoot.File("tests"), "*.csproj", SearchOption.AllDirectories));

    /// <summary>Dockerfile instructions with backslash continuations joined and comments dropped.</summary>
    private static IEnumerable<string> Instructions(string dockerfile) =>
        Regex.Replace(File.ReadAllText(dockerfile).ReplaceLineEndings("\n"), @"\\\n", " ")
            .Split('\n')
            .Where(line => line.Length > 0 && !line.TrimStart().StartsWith('#'));

    /// <summary>External images of FROM lines; references to earlier build stages are skipped.</summary>
    private static IEnumerable<string> BaseImages(string dockerfile)
    {
        var stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var from in Instructions(dockerfile).Where(i => i.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase)))
        {
            var words = from.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(w => !w.StartsWith("--", StringComparison.Ordinal)).ToList();
            if (!stages.Contains(words[0]))
            {
                yield return words[0];
            }

            if (words.Count == 3 && words[1].Equals("AS", StringComparison.OrdinalIgnoreCase))
            {
                stages.Add(words[2]);
            }
        }
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
        foreach (var csproj in ProjectFiles())
        {
            Assert.DoesNotContain("<TargetFramework>", File.ReadAllText(csproj));
        }
    }

    [Fact]
    public void Runtime_image_tag_matches_the_runtime_versions_in_versions_md()
    {
        const string runtimeImage = "mcr.microsoft.com/dotnet/aspnet:";
        var tags = Dockerfiles.SelectMany(BaseImages)
            .Where(image => image.StartsWith(runtimeImage, StringComparison.Ordinal))
            .Select(image => image[runtimeImage.Length..].Split('-', '@')[0])
            .Distinct().ToList();
        var runtime = Assert.Single(tags);   // one runtime version across the app and the lookup mock

        foreach (var framework in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
        {
            var row = Regex.Match(VersionsDoc, $@"\(`{Regex.Escape(framework)}`\)\s*\|\s*([^|\s]+)\s*\|");
            Assert.True(row.Success, $"docs/versions.md has no row for {framework}");
            Assert.Equal(runtime, row.Groups[1].Value);
        }
    }
}
