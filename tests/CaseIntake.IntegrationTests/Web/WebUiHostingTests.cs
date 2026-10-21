using System.Net;
using System.Text.RegularExpressions;
using CaseIntake.Cases;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.TestResults;
using CaseIntake.IntegrationTests.Identity;
using CaseIntake.Persistence;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Web;

/// <summary>The real web host over a migrated database with the seeded demo accounts and a private results folder.</summary>
public sealed class WebUiFixture : PostgresDatabaseFixture, IAsyncLifetime
{
    public string ResultsRoot { get; } = Path.Combine(Path.GetTempPath(), "caseintake-webui-" + Guid.NewGuid().ToString("N"));
    public CaseIntakeWebFactory Factory { get; private set; } = null!;

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await IdentityTestSupport.SeedAsync(ConnectionString);
        Directory.CreateDirectory(ResultsRoot);
        Factory = new CaseIntakeWebFactory(ConnectionString, "http://127.0.0.1:1",
            configureServices: services => services.AddFakeLogging(),
            extraConfiguration: new Dictionary<string, string?>
            {
                ["TestResults:RootPath"] = ResultsRoot,
                ["TestResults:ScanInterval"] = "00:00:00",
                ["Seed:DemoPassword"] = IdentityTestSupport.DemoPassword,
            });
    }

    public new async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
        await base.DisposeAsync();
        if (Directory.Exists(ResultsRoot))
        {
            Directory.Delete(ResultsRoot, recursive: true);
        }
    }

    public BrowserSession NewSession() =>
        new(Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }));
}

/// <summary>
/// Framework script delivery (the Docker regression, in process), Not Found handling that leaves endpoint status codes
/// alone, the case CSV export and the test evidence endpoints' authorization (E5 UI, E5-S8 server-side enforcement).
/// </summary>
[Category("Story:E5-S1")]
[Category("Story:E5-S8")]
[Category("Req:R-4.2-11")]
public sealed partial class WebUiHostingTests : ClassFixture<WebUiHostingTests, WebUiFixture>
{
    private static WebUiFixture fixture => Shared;

    [GeneratedRegex(@"_framework/blazor\.web(\.[a-z0-9]+)?\.js")]
    private static partial Regex FrameworkScript();

    [TestCase("/_framework/blazor.web.js", "javascript")]
    [TestCase("/app.css", "text/css")]
    [TestCase("/js/app.js", "javascript")]
    [TestCase("/js/interop.js", "javascript")]
    [TestCase("/Components/Layout/ReconnectModal.razor.js", "javascript")]
    [TestCase("/manifest.webmanifest", "manifest")]
    [TestCase("/sw.js", "javascript")]
    [TestCase("/offline.html", "text/html")]
    [TestCase("/icons/icon-192.png", "image/png")]
    public async Task Static_assets_are_served_anonymously_with_their_content_type_not_the_sign_in_redirect(string path, string type)
    {
        using var client = fixture.NewSession();

        using var response = await client.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(type, response.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
    }

    [Test]
    public async Task The_offline_page_is_self_contained_so_it_renders_styled_without_the_server()
    {
        // The service worker serves this page when the server is unreachable; only it and the icons are precached, so a
        // linked stylesheet or script would be missing and the page would render unstyled (an unsized icon fills the window).
        using var client = fixture.NewSession();
        var html = await client.Client.GetStringAsync("/offline.html");

        Assert.DoesNotMatch("<link[^>]+rel=\"stylesheet\"", html);
        Assert.DoesNotMatch("<script", html);
        Assert.Contains("<style>", html);
        Assert.Contains("<h1>You're offline</h1>", html);
        Assert.Contains("href=\"/\"", html);
        var icons = Regex.Matches(html, "<svg[^>]*>");
        Assert.NotEmpty(icons);
        Assert.All(icons, svg => Assert.Matches("width=\"\\d+\"[^>]*height=\"\\d+\"", svg.Value));
    }

    [Test]
    public async Task The_sign_in_page_references_one_fingerprinted_framework_script_and_no_inline_script()
    {
        using var client = fixture.NewSession();
        var html = await client.Client.GetStringAsync("/account/login");

        var script = Assert.Single(FrameworkScript().Matches(html));
        using var response = await client.Client.GetAsync("/" + script.Value);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)[^>]*>", html);
    }

    [Test]
    public async Task An_unknown_url_answers_404_with_the_accessible_not_found_page()
    {
        using var client = fixture.NewSession();
        await client.SignInAsync(DemoAccountsHttp.CaseWorker);

        using var response = await client.Client.GetAsync("/definitely/not/here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<h1 tabindex=\"-1\" id=\"page-title\">Page not found</h1>", html);
        Assert.Contains("<title>Page not found - Case Intake and Triage</title>", html);
    }

    [Test]
    public async Task A_matched_endpoint_keeps_its_own_404_without_a_page()
    {
        using var client = fixture.NewSession();
        await client.SignInAsync(DemoAccountsHttp.CaseWorker);

        using var response = await client.Client.GetAsync($"/cases/999999/attachments/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [TestCase("/exports/cases.csv")]
    [TestCase("/test-results/runs/1/export/csv")]
    [TestCase("/test-results/artifacts/0195b3a3-0000-7000-8000-000000000000")]
    public async Task Protected_endpoints_answer_401_to_anonymous_requests_instead_of_redirecting(string path)
    {
        using var client = fixture.NewSession();
        using var response = await client.Client.GetAsync(path);
        // 401, not a 302 to the sign-in page (.NET 10 still adds the login URL as a Location header on the 401).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestCase("/test-results/runs/1/export/html")]
    [TestCase("/test-results/artifacts/0195b3a3-0000-7000-8000-000000000000")]
    public async Task Test_evidence_is_forbidden_to_case_workers_and_supervisors(string path)
    {
        foreach (var user in new[] { DemoAccountsHttp.CaseWorker, DemoAccountsHttp.Supervisor })
        {
            using var client = fixture.NewSession();
            await client.SignInAsync(user);
            using var response = await client.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Test]
    public async Task Test_pages_redirect_users_without_the_permission_to_access_denied()
    {
        using var client = fixture.NewSession();
        await client.SignInAsync(DemoAccountsHttp.CaseWorker);

        using var response = await client.Client.GetAsync("/test-results");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/account/access-denied", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Test]
    public async Task Case_csv_export_uses_the_users_permission_and_neutralizes_formulas()
    {
        var cases = ActivatorUtilities.CreateInstance<CaseService>(fixture.Factory.Services,
            new FakeCurrentUser { Actor = FakeCurrentUser.ForRole(AppRoles.CaseWorker, "seed-user") });
        await cases.CreateAsync(new CreateCaseRequest("=SUM(1,2) exported title", null, Builders.Veteran()), CancellationToken.None);
        using var client = fixture.NewSession();
        await client.SignInAsync(DemoAccountsHttp.QualityAnalyst);

        using var response = await client.Client.GetAsync("/exports/cases.csv?q=exported");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition!.ToString(), StringComparison.Ordinal);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Id,Title,Status,Priority,Category,Owner,DueOn,CreatedAtUtc,UpdatedAtUtc", csv.TrimStart((char)0xFEFF), StringComparison.Ordinal);
        Assert.Contains(",\"'=SUM(1,2) exported title\",New,Medium,\"General\",,", csv);
    }

    [Test]
    public async Task Quality_analyst_gets_evidence_inline_for_preview_and_as_an_attachment_for_download_and_access_is_audited()
    {
        var (artifactId, bytes) = await StageArtifactAsync("shot.png", "image/png", "runs/fixture-run/evidence/a/shot.png");
        using var client = fixture.NewSession();
        await client.SignInAsync(DemoAccountsHttp.QualityAnalyst);

        using var inline = await client.Client.GetAsync($"/test-results/artifacts/{artifactId}");
        using var download = await client.Client.GetAsync($"/test-results/artifacts/{artifactId}?download=true");

        Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
        Assert.Equal("image/png", inline.Content.Headers.ContentType!.MediaType);
        Assert.Null(inline.Content.Headers.ContentDisposition);
        Assert.Equal(bytes, await inline.Content.ReadAsByteArrayAsync());
        Assert.Equal("no-store", inline.Headers.CacheControl!.ToString());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        await using var db = fixture.CreateContext();
        Assert.Equal(["EvidenceDownloaded", "EvidenceViewed"], (await db.TestResultAuditEntries.Where(a => a.ArtifactId == artifactId).Select(a => a.Action).ToListAsync()).Order());
    }

    [Test]
    public async Task Html_evidence_is_never_rendered_inline()
    {
        var (artifactId, _) = await StageArtifactAsync("report.html", "text/html", "runs/fixture-run/report.html");
        using var client = fixture.NewSession();
        await client.SignInAsync(DemoAccountsHttp.QualityAnalyst);

        using var response = await client.Client.GetAsync($"/test-results/artifacts/{artifactId}");

        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Test]
    public async Task An_artifact_path_outside_the_results_store_is_never_served()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);
        var (artifactId, _) = await StageArtifactAsync("x.png", "image/png", Path.GetRelativePath(fixture.ResultsRoot, outside), writeFile: false);
        using var client = fixture.NewSession();
        await client.SignInAsync(DemoAccountsHttp.QualityAnalyst);

        using var response = await client.Client.GetAsync($"/test-results/artifacts/{artifactId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        File.Delete(outside);
    }

    private async Task<(Guid Id, byte[] Bytes)> StageArtifactAsync(string name, string contentType, string relativePath, bool writeFile = true)
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 9, 8, 7];
        if (writeFile)
        {
            var full = Path.Combine(fixture.ResultsRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, bytes);
        }
        await using var db = fixture.CreateContext();
        var environment = await db.TestEnvironments.FirstOrDefaultAsync() ?? db.TestEnvironments.Add(new TestEnvironment { Key = Guid.NewGuid().ToString("N"), Name = "fixture", OperatingSystem = "Linux", Runtime = ".NET" }).Entity;
        var run = new TestRun
        {
            RunKey = "fixture-" + Guid.NewGuid().ToString("N"), StartedAtUtc = DateTimeOffset.UnixEpoch, Runner = "fixture", Environment = environment,
            SourcePath = "runs/fixture-run", ImportedAtUtc = DateTimeOffset.UnixEpoch, ImportedBy = "fixture",
        };
        var artifact = new TestArtifact
        {
            Id = Guid.NewGuid(), Run = run, Kind = TestArtifactKind.Screenshot, FileName = name, ContentType = contentType, SizeBytes = bytes.Length,
            Sha256 = new string('0', 64), RelativePath = relativePath.Replace('\\', '/'), CreatedAtUtc = DateTimeOffset.UnixEpoch,
        };
        db.TestArtifacts.Add(artifact);
        await db.SaveChangesAsync();
        return (artifact.Id, bytes);
    }
}

internal static class DemoAccountsHttp
{
    public const string CaseWorker = "caseworker@example.test";
    public const string Supervisor = "supervisor@example.test";
    public const string QualityAnalyst = "qa@example.test";
}
