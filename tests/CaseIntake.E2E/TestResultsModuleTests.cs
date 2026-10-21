using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// The Test Results module in the browser (AC1, AC2, AC5, AC7, AC8, AC9): a synthetic run (TRX with a passed, a failed and a
/// skipped test, a suite that failed to start, and E2E evidence with a real screenshot) is staged in the web-e2e results
/// folder, imported by the QualityAnalyst, and inspected: metrics, the failure with expected/actual, the screenshot
/// preview with zoom, authorized download, CSV export, and the phone layout.
/// </summary>
public sealed class TestResultsModuleTests : E2ETestBase
{
    private const string FailingTest = "CaseIntake.Fixture.SyntheticTests.Shows_a_failure_with_evidence";

    [Test, FixturesRequired]
    public async Task QualityAnalyst_imports_a_run_and_inspects_failure_evidence()
    {
        await using var s = await StartAsync(Meta("Test results: import and inspect evidence", "Test management", ["R-4.2-13"],
            expected: "Run imported; failed and not-executed tests distinguished; failure detail with screenshot preview, zoom, download and export"));
        var page = s.Page;
        var runKey = $"e2e-fixture-{TimeProvider.System.GetUtcNow():yyyyMMddTHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

        await s.StepAsync("Stage a synthetic run folder (TRX, manifest, evidence with a real screenshot)", async () =>
        {
            await page.GotoAsync("/account/login");
            var png = await page.ScreenshotAsync();
            StageRun(runKey, png);
        });
        await SignInAsync(s, DemoAccounts.QualityAnalyst);
        await s.StepAsync("Open Administration > Quality assurance and import new runs", async () =>
        {
            await PressAsync(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "Quality assurance" }));
            await ExpectPageAsync(page, "Test results");
            await PressAsync(page.GetByTestId("import-runs"));
            await Expect(Announcer(page)).ToContainTextAsync("imported");
        }, expected: "'n run(s) imported' announced");
        await s.ScreenshotAsync("Test results dashboard (desktop)");
        await s.AxeAsync("Test results dashboard");
        await s.StepAsync("Find the run and check its metrics: failed, skipped and not run are distinct", async () =>
        {
            await GotoAsync(page, "/test-results/runs");
            await page.Locator("#run-search").FillAsync(runKey);
            await page.Locator("#run-search").PressAsync("Enter");
            await PressAsync(page.GetByRole(AriaRole.Link, new() { Name = runKey }));
            await ExpectPageAsync(page, $"Run {runKey}");
            await Expect(page.GetByTestId("record-header")).ToContainTextAsync("4 total, 1 passed, 1 failed, 1 skipped, 0 blocked, 1 not run");
            await Expect(page.GetByTestId("suites-table")).ToContainTextAsync("Failed to start");
        });
        await s.AxeAsync("Test run detail");
        await s.StepAsync("Open the failed test: the error, expected and actual results and the steps are shown", async () =>
        {
            await PressAsync(page.GetByTestId("executions-table").GetByRole(AriaRole.Link, new() { Name = "Shows a failure with evidence" }));
            await Expect(page.GetByTestId("execution-outcome")).ToHaveTextAsync("Failed");
            await Expect(page.GetByTestId("failure-message")).ToContainTextAsync("Expected status 'Triaged' but was 'New'");
            await Expect(page.GetByTestId("actual-result")).ToContainTextAsync("Status stayed New");
            await Expect(page.GetByTestId("steps-table")).ToContainTextAsync("Confirm the transition");
            await Expect(page.GetByTestId("console-log")).ToContainTextAsync("[REDACTED-SSN]");
            await Expect(page.GetByTestId("console-log")).Not.ToContainTextAsync("900-12-3456");
        }, expected: "Failure message, expected and actual results, steps; SSN redacted in logs");
        await s.AxeAsync("Test execution detail");
        await s.StepAsync("Preview the screenshot in the dialog, zoom in, then Esc returns focus to the thumbnail", async () =>
        {
            var thumb = page.GetByTestId("screenshots").GetByRole(AriaRole.Button).First;
            await PressAsync(thumb);
            var dialog = page.GetByTestId("evidence-preview");
            await Expect(dialog).ToBeVisibleAsync();
            await Expect(dialog.Locator("img")).ToBeVisibleAsync();
            await PressAsync(dialog.GetByRole(AriaRole.Button, new() { Name = "Zoom in" }));
            await Expect(dialog).ToContainTextAsync("75%");
            await page.Keyboard.PressAsync("Escape");
            await Expect(dialog).ToBeHiddenAsync();
            await E2ESession.ExpectFocusedAsync(thumb);
        });
        await s.StepAsync("Evidence and exports download only through the authorized endpoints", async () =>
        {
            var src = await page.GetByTestId("screenshots").Locator("img").First.GetAttributeAsync("src");
            var image = await s.Context.APIRequest.GetAsync(src!);
            Assert.Equal(200, image.Status);
            Assert.Equal("image/png", image.Headers["content-type"]);
            Assert.Equal("no-store", image.Headers["cache-control"]);
            var runHref = await page.GetByRole(AriaRole.Link, new() { Name = runKey }).First.GetAttributeAsync("href");
            var runId = runHref!.Split('/')[^1];
            var csv = await s.Context.APIRequest.GetAsync($"/test-results/runs/{runId}/export/csv");
            Assert.Equal(200, csv.Status);
            Assert.Contains("Shows a failure with evidence", await csv.TextAsync(), StringComparison.Ordinal);
            var html = await s.Context.APIRequest.GetAsync($"/test-results/runs/{runId}/export/html");
            Assert.Equal(200, html.Status);
            Assert.Contains("attachment", html.Headers["content-disposition"], StringComparison.Ordinal);
        });
        s.Complete();
    }

    [Test, FixturesRequired]
    public async Task Test_results_dashboard_works_on_a_phone()
    {
        await using var s = await StartAsync(Meta("Test results on a phone", "Test management", ["R-4.2-12"],
            expected: "KPI cards, charts and run cards fit the phone width"), Viewports.Phone390, mobile: true);
        var page = s.Page;
        StageRun($"e2e-fixture-mobile-{Guid.NewGuid().ToString("N")[..8]}", null);
        await SignInAsync(s, DemoAccounts.QualityAnalyst);
        await s.StepAsync("Dashboard KPIs and charts at 390 px without horizontal overflow", async () =>
        {
            await GotoAsync(page, "/test-results");
            await PressAsync(page.GetByTestId("import-runs"));
            await Expect(page.GetByTestId("tr-kpi-total")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("tr-chart-trend").Locator("svg[role=img]")).ToBeVisibleAsync();
            var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
            Assert.True(overflow <= 0, $"Overflow {overflow}px");
        });
        await s.ScreenshotAsync("Test results dashboard on a phone");
        await s.AxeAsync("Test results dashboard (phone)");
        s.Complete();
    }

    /// <summary>Writes runs/&lt;key&gt;/ with run.json, a TRX file and one E2E evidence folder, as scripts/run-tests.sh would.</summary>
    private static void StageRun(string runKey, byte[]? screenshot)
    {
        var folder = Path.Combine(E2ESettings.FixturesRoot!, "runs", runKey);
        Directory.CreateDirectory(folder);
        var start = TimeProvider.System.GetUtcNow().AddMinutes(-2);
        File.WriteAllText(Path.Combine(folder, "Fixture.trx"), Trx(start));
        if (screenshot is not null)
        {
            var evidence = Path.Combine(folder, "evidence", "fixture-failure");
            Directory.CreateDirectory(evidence);
            File.WriteAllBytes(Path.Combine(evidence, "01-failure.png"), screenshot);
            File.WriteAllText(Path.Combine(evidence, "evidence.json"), JsonSerializer.Serialize(new
            {
                fullName = FailingTest,
                title = "Shows a failure with evidence",
                story = "E5-S6",
                requirements = new[] { "R-4.2-8" },
                priority = "High",
                description = "Synthetic failing test staged by the E2E suite.",
                preconditions = "A New case exists.",
                expected = "Status changes to Triaged.",
                actual = "Status stayed New.",
                browser = "chromium (fixture)",
                viewport = "1440x900",
                outcome = "Failed",
                steps = new object[]
                {
                    new { name = "Open the case", outcome = "Passed", durationMs = 120 },
                    new { name = "Confirm the transition", outcome = "Failed", durationMs = 80, expected = "Status: Triaged", actual = "Status: New", message = "Expected status 'Triaged' but was 'New'" },
                },
                artifacts = new[] { new { kind = "Screenshot", file = "01-failure.png", caption = "Failure: Confirm the transition" } },
                consoleErrors = new[] { "console.error: lookup for 900-12-3456 failed" },
                networkFailures = new[] { "POST /_blazor failed: net::ERR_ABORTED" },
            }));
        }
        File.WriteAllText(Path.Combine(folder, "run.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            runKey,
            startedAtUtc = start,
            finishedAtUtc = start.AddSeconds(42),
            runner = "e2e fixture (TestResultsModuleTests)",
            status = "Partial",
            git = new { branch = "e2e-fixture", commit = "0000000fixture", buildId = "fixture" },
            imageVersion = "fixture",
            environment = new { name = "e2e fixture", os = "Linux", runtime = ".NET 10", baseUrl = E2ESettings.BaseUrl },
            suites = new object[]
            {
                new { name = "CaseIntake.Fixture", type = "EndToEnd", trx = "Fixture.trx", exitCode = 1, status = "Completed" },
                new { name = "CaseIntake.FixtureNotStarted", type = "Integration", status = "FailedToStart", exitCode = 1, message = "The test host could not start." },
            },
        }));
        // The not-started suite has one known test from an earlier (synthetic) run, so it appears as Not Run.
        var seed = Path.Combine(E2ESettings.FixturesRoot!, "runs", "e2e-fixture-0000-seed");
        if (!Directory.Exists(seed))
        {
            Directory.CreateDirectory(seed);
            File.WriteAllText(Path.Combine(seed, "Seed.trx"), Trx(start.AddDays(-1), "CaseIntake.FixtureNotStarted.SeedTests.Known_test_of_the_failing_suite", "Passed"));
            File.WriteAllText(Path.Combine(seed, "run.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, runKey = "e2e-fixture-0000-seed", startedAtUtc = start.AddDays(-1), finishedAtUtc = start.AddDays(-1).AddSeconds(5),
                runner = "e2e fixture seed", status = "Completed",
                environment = new { name = "e2e fixture", os = "Linux", runtime = ".NET 10", baseUrl = E2ESettings.BaseUrl },
                suites = new[] { new { name = "CaseIntake.FixtureNotStarted", type = "Integration", trx = "Seed.trx", exitCode = 0, status = "Completed" } },
            }));
        }
    }

    private static string Trx(DateTimeOffset start, string? onlyTest = null, string? onlyOutcome = null)
    {
        var tests = onlyTest is null
            ? new[]
            {
                ("CaseIntake.Fixture.SyntheticTests.Passes_quickly", "Passed", (string?)null),
                (FailingTest, "Failed", "Expected status 'Triaged' but was 'New'"),
                ("CaseIntake.Fixture.SyntheticTests.Is_skipped_on_purpose", "NotExecuted", null),
            }
            : [(onlyTest, onlyOutcome!, null)];
        var definitions = string.Concat(tests.Select((t, i) =>
            $"<UnitTest name=\"{t.Item1}\" id=\"00000000-0000-0000-0000-00000000000{i}\"><TestMethod className=\"{t.Item1[..t.Item1.LastIndexOf('.')]}\" name=\"{t.Item1[(t.Item1.LastIndexOf('.') + 1)..]}\" /></UnitTest>"));
        var results = string.Concat(tests.Select((t, i) =>
            $"<UnitTestResult testId=\"00000000-0000-0000-0000-00000000000{i}\" testName=\"{t.Item1}\" outcome=\"{t.Item2}\" duration=\"00:00:01.2500000\" " +
            $"startTime=\"{start.AddSeconds(i).ToString("o", CultureInfo.InvariantCulture)}\" endTime=\"{start.AddSeconds(i + 1).ToString("o", CultureInfo.InvariantCulture)}\">" +
            (t.Item3 is null ? "" : $"<Output><ErrorInfo><Message>{t.Item3}</Message><StackTrace>at Fixture.Line(42)</StackTrace></ErrorInfo></Output>") +
            "</UnitTestResult>"));
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">" +
            $"<TestDefinitions>{definitions}</TestDefinitions><Results>{results}</Results></TestRun>";
    }
}
