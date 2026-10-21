using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// The Docker-hosted app delivers the Blazor framework script and starts interactivity (the regression behind the
/// "blazor.web.js does not work in Docker" defect: the container's content root lacked wwwroot, so every asset request
/// fell through to the sign-in redirect).
/// </summary>
public sealed partial class FrameworkDeliveryTests : E2ETestBase
{
    [GeneratedRegex(@"_framework/blazor\.web(\.[a-z0-9]+)?\.js")]
    private static partial Regex FrameworkScript();

    [Test]
    public async Task Framework_script_is_served_anonymously_as_JavaScript_and_referenced_once()
    {
        await using var s = await StartAsync(Meta("blazor.web.js is delivered by the Docker-hosted app", "Docker JavaScript fix", ["R-4.5-1", "R-4.2-12"],
            expected: "HTTP 200 with a JavaScript content type for the framework script and the app assets; one script reference"));
        await s.StepAsync("Request the framework script and app assets without signing in", async () =>
        {
            foreach (var path in new[] { "/_framework/blazor.web.js", "/app.css", "/js/app.js", "/js/interop.js", "/manifest.webmanifest", "/sw.js", "/offline.html" })
            {
                var response = await s.Context.APIRequest.GetAsync(path, new APIRequestContextOptions { MaxRedirects = 0 });
                Assert.True(response.Status == 200, $"{path} returned HTTP {response.Status}");
                if (path.EndsWith(".js", StringComparison.Ordinal))
                {
                    Assert.Matches("javascript", response.Headers["content-type"]);
                }
            }
            var framework = await s.Context.APIRequest.GetAsync("/_framework/blazor.web.js");
            Assert.Contains("Blazor", await framework.TextAsync(), StringComparison.Ordinal);
        }, expected: "200 and text/javascript for every asset");

        await s.StepAsync("The sign-in page references exactly one framework bootstrap script", async () =>
        {
            var html = await (await s.Context.APIRequest.GetAsync("/account/login")).TextAsync();
            Assert.Single(FrameworkScript().Matches(html));
        });
        s.Complete("Framework script and assets delivered; one bootstrap reference");
    }

    [Test]
    public async Task Interactivity_starts_without_console_errors_or_failed_framework_requests()
    {
        await using var s = await StartAsync(Meta("Blazor interactivity initializes in Docker", "Docker JavaScript fix", ["R-4.5-1", "R-4.5-3"],
            expected: "Circuit connects over WebSockets, buttons respond, navigation works after a full reload, no console errors"));
        await SignInAsync(s, DemoAccounts.CaseWorker);

        await s.StepAsync("A circuit is connected over WebSockets", async () =>
        {
            var socket = await s.Page.RunAndWaitForWebSocketAsync(() => s.Page.ReloadAsync());
            Assert.Contains("/_blazor", socket.Url, StringComparison.Ordinal);
            await WaitForCircuitAsync(s.Page);
        });

        await s.StepAsync("An interactive button responds (collapse the navigation)", async () =>
        {
            var toggle = s.Page.GetByTestId("nav-toggle");
            await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
            await PressAsync(toggle);
            await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
            await Expect(s.Page.GetByTestId("side-nav")).ToHaveClassAsync(new Regex("app-nav--collapsed"));
            await PressAsync(toggle);
            await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        });

        await s.StepAsync("Navigation works after a full page reload", async () =>
        {
            await s.Page.ReloadAsync();
            await WaitForCircuitAsync(s.Page);
            await PressAsync(s.Page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "All cases" }));
            await ExpectPageAsync(s.Page, "Cases");
            await Expect(s.Page.GetByTestId("case-table")).ToBeVisibleAsync();
        });

        await s.StepAsync("No JavaScript errors and no failed framework or circuit requests", () =>
        {
            Assert.Empty(s.ConsoleErrors);
            // Blazor posts /_blazor/disconnect as the page unloads; a full reload aborts that beacon in the browser
            // (Playwright 1.52 reports it as net::ERR_ABORTED). It is the old page saying goodbye, not a failed request.
            var failures = s.NetworkFailures.Where(f => !(f.StartsWith("POST /_blazor/disconnect", StringComparison.Ordinal) && f.EndsWith("net::ERR_ABORTED", StringComparison.Ordinal)));
            Assert.DoesNotContain(failures, f => f.Contains("/_framework/", StringComparison.Ordinal) || f.Contains("/_blazor", StringComparison.Ordinal) || f.EndsWith(".js -> HTTP 404", StringComparison.Ordinal));
            return Task.CompletedTask;
        }, expected: "0 console errors");
        s.Complete("Interactive, navigable and error-free");
    }

    [Test]
    public async Task Installable_manifest_and_service_worker_without_caching_sensitive_pages()
    {
        await using var s = await StartAsync(Meta("PWA manifest and safe service worker", "Mobile and PWA", ["R-4.2-12"],
            expected: "Manifest with icons and standalone display; the worker caches only static assets"));
        await s.StepAsync("The manifest is installable (name, start URL, standalone, icons)", async () =>
        {
            var manifest = await (await s.Context.APIRequest.GetAsync("/manifest.webmanifest")).JsonAsync();
            Assert.Equal("Case Intake and Triage", manifest!.Value.GetProperty("name").GetString());
            Assert.Equal("standalone", manifest.Value.GetProperty("display").GetString());
            Assert.True(manifest.Value.GetProperty("icons").GetArrayLength() >= 3);
            foreach (var icon in manifest.Value.GetProperty("icons").EnumerateArray())
            {
                Assert.Equal(200, (await s.Context.APIRequest.GetAsync(icon.GetProperty("src").GetString()!)).Status);
            }
        });
        await s.StepAsync("The service worker never caches pages, sign-in or downloads", async () =>
        {
            var worker = await (await s.Context.APIRequest.GetAsync("/sw.js")).TextAsync();
            Assert.Contains("request.mode === 'navigate'", worker, StringComparison.Ordinal);
            Assert.Contains("/^\\/account\\//i", worker, StringComparison.Ordinal);
            Assert.Contains("/^\\/test-results\\//i", worker, StringComparison.Ordinal);
        });
        s.Complete();
    }
}
