using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.Web.Components.Pages.TestResults;
using CaseIntake.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Test results browsing pages: test cases, test case history, evidence gallery, history and trends, run list filters and
/// paging, run detail comparison and files, and the loading, empty and error states of each.
/// </summary>
[Category("Story:Test management")]
[Category("Req:R-4.2-13")]
public class TestResultsBrowsingTests : WebTestContext
{
    private static readonly Error Unavailable = new(ErrorKind.ExternalUnavailable, "Unavailable", "The test results store is unavailable.");

    public TestResultsBrowsingTests() => SignInAs(AppRoles.QualityAnalyst);

    private static TestExecutionRowDto Row(long id, string title, TestOutcome outcome = TestOutcome.Failed) =>
        new(id, 4, id, title, $"Ns.C.{id}", "E2E", TestType.EndToEnd, outcome, TimeSpan.FromMilliseconds(400), "chromium", "390x844", outcome == TestOutcome.Failed ? "Boom" : null, 0);

    // ---- Test cases ----

    [Test]
    public void Test_cases_lists_each_case_with_its_last_status_story_and_requirements()
    {
        TestResults.TestCases.Add(FakeTestResultsService.TestCase(11, "Confirm the transition", TestOutcome.Failed));
        TestResults.TestCases.Add(FakeTestResultsService.TestCase(12, "Never run", last: null, story: null, requirements: null));

        var cut = Render<TestCases>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=cases-table] tbody tr").Count));
        var first = cut.FindAll("[data-testid=cases-table] tbody tr")[0];
        Assert.Equal("test-results/cases/11", first.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Failed", first.QuerySelector(".status-pill")!.TextContent);
        Assert.Contains("E2E", first.TextContent);
        Assert.Contains("R-4.2-8", first.TextContent);
        // A test that has never run reads "Not Run".
        Assert.Equal("Not Run", cut.FindAll("[data-testid=cases-table] tbody tr")[1].QuerySelector(".status-pill")!.TextContent);
        Assert.Equal(["", "E2E", "Web.Tests"], cut.FindAll("#testcase-module option").Select(o => o.GetAttribute("value")));
        Assert.Equal("page", cut.Find("[data-testid=tr-tabs] a[aria-current]").GetAttribute("aria-current"));
        Assert.Equal("Test cases", cut.Find("[data-testid=tr-tabs] a[aria-current]").TextContent);
    }

    [Test]
    public void Test_case_filters_are_sent_to_the_service_and_reset_to_the_first_page()
    {
        var cut = Render<TestCases>();
        cut.WaitForElement("[data-testid=cases-empty]");
        Assert.Contains("No test cases match", cut.Find("[data-testid=cases-empty]").TextContent);

        // Filters apply as soon as they change (the search after a short pause) and always return to page 1.
        Navigation.NavigateTo("test-results/cases?page=3");
        cut.WaitForAssertion(() => Assert.Equal(3, TestResults.CaseQueries.Last().Page));
        cut.Find("#testcase-module").Change("E2E");
        Assert.Equal(1, TestResults.CaseQueries.Last().Page);
        cut.Find("#testcase-type").Change(nameof(TestType.Component));
        cut.Find("#testcase-outcome").Change(nameof(TestOutcome.NotExecuted));
        cut.Find("#testcase-search").Input("  transition ");

        cut.WaitForAssertion(() => Assert.Equal(
            new TestCaseQuery("E2E", TestType.Component, TestOutcome.NotExecuted, "transition", 1, 50), TestResults.CaseQueries.Last()), TimeSpan.FromSeconds(5));

        cut.Find("#testcase-module").Change("");
        cut.Find("#testcase-search").Input("   ");
        cut.WaitForAssertion(() => Assert.Null(TestResults.CaseQueries.Last().Search), TimeSpan.FromSeconds(5));
        Assert.Null(TestResults.CaseQueries.Last().Module);
    }

    [Test]
    public void Test_cases_page_through_results_with_the_pager()
    {
        for (var i = 1; i <= 60; i++)
        {
            TestResults.TestCases.Add(FakeTestResultsService.TestCase(i, $"Case {i:00}"));
        }

        var cut = Render<TestCases>();
        cut.WaitForAssertion(() => Assert.Equal(50, cut.FindAll("[data-testid=cases-table] tbody tr").Count));
        Assert.Equal("Showing 1–50 of 60 test cases", cut.Find("[data-testid=pager-range]").TextContent);
        Assert.Equal("Page 1 of 2", cut.Find("[data-testid=pager-info]").TextContent);

        cut.Find("[data-testid=pager-next]").Click();

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll("[data-testid=cases-table] tbody tr").Count));
        Assert.Equal(2, TestResults.CaseQueries.Last().Page);
        Assert.True(cut.Find("[data-testid=pager-next]").HasAttribute("disabled"));
        // The activated button is now disabled, so focus moves to the other one.
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Test]
    public void Test_cases_show_the_service_error_or_a_generic_message_when_the_call_throws()
    {
        TestResults.Failure = Unavailable;
        var failed = Render<TestCases>();
        failed.WaitForAssertion(() => Assert.Contains("The test results store is unavailable.", failed.Find("[data-testid=cases-error]").TextContent));
        Assert.Empty(failed.FindAll("[data-testid=cases-table]"));

        TestResults.Failure = null;
        TestResults.Throw = new InvalidOperationException("db down");
        TestResults.Only = [nameof(ITestResultsService.ListTestCasesAsync)];
        var thrown = Render<TestCases>();
        thrown.WaitForAssertion(() => Assert.Contains("The test cases could not be loaded. Try again.", thrown.Find("[data-testid=cases-error]").TextContent));
        // Modules still loaded: only the list call failed.
        Assert.Equal(3, thrown.FindAll("#testcase-module option").Count);
    }

    [Test]
    public void Test_cases_show_a_loading_indicator_until_the_service_answers()
    {
        TestResults.Gate = new TaskCompletionSource();
        TestResults.Only = [nameof(ITestResultsService.ListTestCasesAsync)];
        var cut = Render<TestCases>();
        // The summary counts have their own loading indicator; the list says it is loading test cases.
        Assert.Contains(cut.FindAll("[data-testid=loading]"), l => l.TextContent.Contains("Loading test cases…", StringComparison.Ordinal));

        TestResults.Gate.SetResult();

        cut.WaitForElement("[data-testid=cases-empty]");
        Assert.Empty(cut.FindAll("[data-testid=loading]"));
    }

    // ---- Test case detail ----

    [Test]
    public void Test_case_detail_lists_runs_newest_first_with_a_duration_trend_and_the_case_fields()
    {
        var testCase = FakeTestResultsService.TestCase(11, "Confirm the transition", TestOutcome.Failed);
        TestResults.Histories[11] = new TestCaseHistoryDto(testCase,
        [
            new TestHistoryPointDto(5, "run-5", new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero), TestOutcome.Failed, TimeSpan.FromSeconds(3.25), "0123456789abcdef", 51),
            new TestHistoryPointDto(4, "run-4", new DateTimeOffset(2026, 10, 4, 13, 0, 0, TimeSpan.Zero), TestOutcome.Passed, TimeSpan.FromMilliseconds(800), null, 41),
        ]);

        var cut = Render<TestCaseDetail>(p => p.Add(c => c.Id, 11));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=history-table] tbody tr").Count));
        var rows = cut.FindAll("[data-testid=history-table] tbody tr");
        Assert.Equal("test-results/executions/51", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("10-05-2026 09:00 ET", rows[0].TextContent);
        Assert.Contains("3.3 s", rows[0].TextContent);
        Assert.Contains("0123456789", rows[0].TextContent);
        Assert.DoesNotContain("0123456789a", rows[0].TextContent);
        Assert.Contains("800 ms", rows[1].TextContent);
        Assert.Contains("unknown", rows[1].TextContent);
        // The chart reads oldest to newest.
        Assert.Equal(["10-04 09:00 ET", "10-05 09:00 ET"], cut.FindAll("[data-testid=testcase-chart-duration] table tbody th").Select(th => th.TextContent.Trim()));
        Assert.Contains("Ns.Class.Confirm_the_transition", cut.Find("[aria-labelledby=testcase-heading]").TextContent);
        Assert.Equal("Confirm the transition", cut.Find("h1").TextContent.Trim());
    }

    [Test]
    public void Test_case_without_runs_or_recorded_details_says_so()
    {
        TestResults.Histories[12] = new TestCaseHistoryDto(FakeTestResultsService.TestCase(12, "Never run", last: null, story: null, requirements: null) with { Description = null, Priority = null }, []);

        var cut = Render<TestCaseDetail>(p => p.Add(c => c.Id, 12));

        cut.WaitForElement("[data-testid=history-empty]");
        var facts = cut.Find("[aria-labelledby=testcase-heading]").TextContent;
        Assert.Contains("Unknown", facts);
        // Description, story, requirements, priority, test method and workflow.
        Assert.Equal(6, facts.Split("Not recorded").Length - 1);
        Assert.Empty(cut.FindAll("[data-testid=testcase-chart-duration]"));
    }

    [Test]
    public void Missing_or_failing_test_case_shows_an_error_instead_of_the_record()
    {
        var missing = Render<TestCaseDetail>(p => p.Add(c => c.Id, 99));
        missing.WaitForAssertion(() => Assert.Contains("The test case was not found.", missing.Find("[data-testid=testcase-error]").TextContent));
        Assert.Equal("Test case", missing.Find("h1").TextContent.Trim());
        Assert.Empty(missing.FindAll("[data-testid=history-table]"));

        TestResults.Histories[1] = new TestCaseHistoryDto(FakeTestResultsService.TestCase(1, "Any"), []);
        TestResults.Throw = new InvalidOperationException("boom");
        var thrown = Render<TestCaseDetail>(p => p.Add(c => c.Id, 1));
        thrown.WaitForAssertion(() => Assert.Contains("The test case could not be loaded. Try again.", thrown.Find("[data-testid=testcase-error]").TextContent));
    }

    [Test]
    public void Test_case_detail_shows_loading_until_the_history_arrives()
    {
        TestResults.Histories[1] = new TestCaseHistoryDto(FakeTestResultsService.TestCase(1, "Slow"), []);
        TestResults.Gate = new TaskCompletionSource();

        var cut = Render<TestCaseDetail>(p => p.Add(c => c.Id, 1));
        Assert.Contains("Loading test case…", cut.Find("[data-testid=loading]").TextContent);

        TestResults.Gate.SetResult();
        cut.WaitForElement("[data-testid=history-empty]");
        Assert.Empty(cut.FindAll("[data-testid=loading]"));
    }

    // ---- Evidence gallery ----

    [Test]
    public void Evidence_gallery_shows_screenshots_with_preview_links_to_the_test_and_downloads()
    {
        TestResults.Runs.Add(FakeTestResultsService.Run(4, passed: 1, failed: 1));
        var shot = FakeTestResultsService.Artifact(TestArtifactKind.Screenshot, "01-failure.png", "Failure: Confirm");
        var purged = FakeTestResultsService.Artifact(TestArtifactKind.Screenshot, "old.png", purged: true);
        var log = FakeTestResultsService.Artifact(TestArtifactKind.ServerLog, "server.log");
        TestResults.Artifacts.Add(new TestArtifactListItemDto(shot, 4, "run-4", 7, "Confirm the transition", TestOutcome.Failed));
        TestResults.Artifacts.Add(new TestArtifactListItemDto(purged, 4, "run-4", 8, null, null));
        TestResults.Artifacts.Add(new TestArtifactListItemDto(log, 4, "run-4", null, null, null));

        var cut = Render<Evidence>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=evidence-gallery] li").Count));
        Assert.Equal(new TestArtifactQuery(Kind: TestArtifactKind.Screenshot), TestResults.ArtifactQueries.Single());
        Assert.Equal(["", "4"], cut.FindAll("#evidence-run option").Select(o => o.GetAttribute("value")));
        var items = cut.FindAll("[data-testid=evidence-gallery] li");
        Assert.Equal("Preview screenshot: Failure: Confirm", items[0].QuerySelector("button")!.GetAttribute("aria-label"));
        Assert.Equal("test-results/executions/7", items[0].QuerySelector(".thumb__meta a")!.GetAttribute("href"));
        Assert.Contains("Screenshot · 2.0 KB", items[0].TextContent);
        Assert.Contains("· Failed", items[0].TextContent);
        Assert.Null(items[1].QuerySelector("button"));
        Assert.Contains("removed by retention", items[1].TextContent);
        Assert.Contains("Test result", items[1].TextContent);
        Assert.DoesNotContain("Download", items[1].TextContent);
        Assert.Equal("test-results/runs/4", items[2].QuerySelector(".thumb__meta a")!.GetAttribute("href"));
        Assert.Contains("Run run-4", items[2].TextContent);
        Assert.Contains("Server log", items[2].TextContent);
        Assert.Equal($"/test-results/artifacts/{log.Id}?download=true", items[2].QuerySelector("a[download]")!.GetAttribute("href"));

        items[0].QuerySelector("button")!.Click();

        InteropModule.VerifyInvoke("showDialog");
        Assert.Equal("Failure: Confirm", cut.Find("[data-testid=evidence-preview] img").GetAttribute("alt"));
        Assert.Equal($"/test-results/artifacts/{shot.Id}?download=true", cut.Find("[data-testid=evidence-preview] .modal__footer a").GetAttribute("href"));
    }

    [Test]
    public void Evidence_filters_send_run_kind_and_search_and_paging_keeps_them()
    {
        TestResults.Runs.Add(FakeTestResultsService.Run(4, passed: 1, failed: 0));
        for (var i = 0; i < 50; i++)
        {
            TestResults.Artifacts.Add(new TestArtifactListItemDto(FakeTestResultsService.Artifact(TestArtifactKind.Trace, $"trace-{i}.zip"), 4, "run-4", null, null, null));
        }
        var cut = Render<Evidence>();
        cut.WaitForElement("[data-testid=evidence-gallery]");

        cut.Find("#evidence-search").Input(" trace ");
        cut.Find("#evidence-run").Change("4");
        cut.Find("#evidence-kind").Change(nameof(TestArtifactKind.Trace));
        cut.Find("[data-testid=evidence-filters]").Submit();
        Assert.Equal(new TestArtifactQuery(4, TestArtifactKind.Trace, "trace", 1, 48), TestResults.ArtifactQueries.Last());

        cut.Find("[data-testid=pager-next]").Click();
        Assert.Equal(new TestArtifactQuery(4, TestArtifactKind.Trace, "trace", 2, 48), TestResults.ArtifactQueries.Last());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=evidence-gallery] li").Count));

        cut.Find("#evidence-search").Input("");
        cut.Find("#evidence-kind").Change("");
        cut.Find("[data-testid=evidence-filters]").Submit();
        Assert.Equal(new TestArtifactQuery(4, null, null, 1, 48), TestResults.ArtifactQueries.Last());
    }

    [Test]
    public void Evidence_shows_the_empty_state_an_error_or_a_generic_failure()
    {
        var empty = Render<Evidence>();
        empty.WaitForAssertion(() => Assert.Contains("No evidence files match", empty.Find("[data-testid=evidence-empty]").TextContent));

        TestResults.Failure = Unavailable;
        var failed = Render<Evidence>();
        failed.WaitForAssertion(() => Assert.Contains("unavailable", failed.Find("[data-testid=evidence-error]").TextContent));
        Assert.Single(failed.FindAll("#evidence-run option"));

        TestResults.Failure = null;
        TestResults.Throw = new InvalidOperationException("boom");
        TestResults.Only = [nameof(ITestResultsService.ListArtifactsAsync)];
        var thrown = Render<Evidence>();
        thrown.WaitForAssertion(() => Assert.Contains("The evidence could not be loaded. Try again.", thrown.Find("[data-testid=evidence-error]").TextContent));
        Assert.Empty(thrown.FindAll("[data-testid=evidence-gallery]"));
    }

    [Test]
    public void Evidence_shows_loading_until_the_gallery_arrives()
    {
        TestResults.Gate = new TaskCompletionSource();
        TestResults.Only = [nameof(ITestResultsService.ListArtifactsAsync)];
        var cut = Render<Evidence>();
        Assert.Contains("Loading evidence…", cut.Find("[data-testid=loading]").TextContent);
        TestResults.Gate.SetResult();
        cut.WaitForElement("[data-testid=evidence-empty]");
        Assert.Empty(cut.FindAll("[data-testid=loading]"));
    }

    // ---- Evidence preview ----

    [Test]
    public void Preview_zooms_through_the_steps_and_returns_to_fit()
    {
        var shot = FakeTestResultsService.Artifact(TestArtifactKind.Screenshot, "page.png");
        TestResults.Artifacts.Add(new TestArtifactListItemDto(shot, 4, "run-4", 7, "Page", TestOutcome.Passed));
        var cut = Render<Evidence>();
        cut.WaitForElement("[data-testid=evidence-gallery] button").Click();

        string Zoom() => cut.Find("[data-testid=zoom-level]").TextContent;
        Assert.Equal("Fit to window", Zoom());
        Assert.Equal("page.png", cut.Find("[data-testid=evidence-preview] img").GetAttribute("alt"));
        Assert.Equal("true", cut.Find("[data-testid=evidence-preview] .btn-row button").GetAttribute("aria-pressed"));

        cut.Find("[aria-label='Zoom in']").Click();
        Assert.Equal("75%", Zoom());
        cut.Find("[aria-label='Zoom in']").Click();
        Assert.Equal("100%", Zoom());
        Assert.Equal("true", cut.FindAll("[data-testid=evidence-preview] .btn-row button")[1].GetAttribute("aria-pressed"));
        for (var i = 0; i < 4; i++)
        {
            cut.Find("[aria-label='Zoom in']").Click();
        }
        Assert.Equal("300%", Zoom());
        Assert.True(cut.Find("[aria-label='Zoom in']").HasAttribute("disabled"));

        cut.FindAll("[data-testid=evidence-preview] .btn-row button")[0].Click();
        Assert.Equal("Fit to window", Zoom());
        cut.Find("[aria-label='Zoom out']").Click();
        Assert.Equal("50%", Zoom());
        cut.Find("[aria-label='Zoom out']").Click();
        Assert.Equal("25%", Zoom());
        Assert.True(cut.Find("[aria-label='Zoom out']").HasAttribute("disabled"));
        cut.FindAll("[data-testid=evidence-preview] .btn-row button")[1].Click();
        Assert.Equal("100%", Zoom());

        Assert.Contains(InteropModule.Invocations, i => i.Identifier == "setZoom" && i.Arguments[1] is 0.25);

        cut.Find("[data-testid=evidence-preview] [aria-label=Close]").Click();

        InteropModule.VerifyInvoke("closeDialog");
        Assert.Empty(cut.FindAll("[data-testid=zoom-level]"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    // ---- History and trends ----

    [Test]
    public void History_charts_recent_runs_oldest_first_and_lists_flaky_tests()
    {
        TestResults.Runs.Add(FakeTestResultsService.Run(3, passed: 9, failed: 1, notExecuted: 2));
        TestResults.Runs.Add(FakeTestResultsService.Run(2, passed: 10, failed: 0));
        TestResults.Flaky.Add(new FlakyTestDto(11, "Sometimes fails", "E2E", 5, 3, 2, TestOutcome.Failed));

        var cut = Render<History>();

        cut.WaitForElement("[data-testid=flaky-table]");
        Assert.Equal(new TestRunQuery(1, 20), TestResults.RunQueries.Single());
        Assert.Equal(["10-02 05:00 ET", "10-03 05:00 ET"], cut.FindAll("[data-testid=history-chart-passrate] table tbody th").Select(th => th.TextContent.Trim()));
        Assert.Contains("The last 20 imported runs", cut.Markup);
        var row = cut.Find("[data-testid=flaky-table] tbody tr");
        Assert.Equal("test-results/cases/11", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(["Sometimes fails", "E2E", "5", "3", "2", "Failed"], row.QuerySelectorAll("td").Select(td => td.TextContent.Trim()));
        Assert.Equal("History and trends", cut.Find("[data-testid=tr-tabs] a[aria-current]").TextContent);
    }

    [Test]
    public void History_without_flaky_tests_or_runs_shows_empty_states()
    {
        TestResults.Runs.Add(FakeTestResultsService.Run(2, passed: 10, failed: 0));
        var stable = Render<History>();
        stable.WaitForAssertion(() => Assert.Contains("No flaky or recurring failures", stable.Find("[data-testid=flaky-empty]").TextContent));

        TestResults.Runs.Clear();
        var none = Render<History>();
        none.WaitForAssertion(() => Assert.Contains("scripts/run-all-tests.sh", none.Find("[data-testid=history-empty]").TextContent));
        Assert.Empty(none.FindAll("[data-testid=history-chart-passrate]"));
    }

    [Test]
    public void History_failures_show_an_alert_and_flaky_failures_fall_back_to_empty()
    {
        TestResults.Failure = Unavailable;
        var failed = Render<History>();
        failed.WaitForAssertion(() => Assert.Contains("unavailable", failed.Find("[data-testid=history-error]").TextContent));
        Assert.Empty(failed.FindAll("[data-testid=history-empty]"));

        TestResults.Runs.Add(FakeTestResultsService.Run(2, passed: 10, failed: 0));
        TestResults.Only = [nameof(ITestResultsService.GetFlakyTestsAsync)];
        var flakyFailed = Render<History>();
        flakyFailed.WaitForElement("[data-testid=flaky-empty]");
        Assert.Empty(flakyFailed.Find("[data-testid=history-error]").TextContent.Trim());

        TestResults.Failure = null;
        TestResults.Throw = new InvalidOperationException("boom");
        TestResults.Only = null;
        var thrown = Render<History>();
        thrown.WaitForAssertion(() => Assert.Contains("The history could not be loaded. Try again.", thrown.Find("[data-testid=history-error]").TextContent));
    }

    [Test]
    public void History_shows_loading_until_the_runs_arrive()
    {
        TestResults.Gate = new TaskCompletionSource();
        var cut = Render<History>();
        Assert.Contains("Loading history…", cut.Find("[data-testid=loading]").TextContent);
        TestResults.Gate.SetResult();
        cut.WaitForElement("[data-testid=history-empty]");
    }

    // ---- Test runs list ----

    [Test]
    public void Run_filters_are_sent_to_the_service_and_the_filtered_empty_state_suggests_fewer_filters()
    {
        var cut = Render<TestRuns>();
        cut.WaitForAssertion(() => Assert.Contains("scripts/run-all-tests.sh", cut.Find("[data-testid=runs-empty]").TextContent));
        Assert.Equal(["", "docker compose tests"], cut.FindAll("#run-environment option").Select(o => o.GetAttribute("value")));
        Assert.Equal(["Completed", "Partial", "Failed to start"], cut.FindAll("#run-status option").Skip(1).Select(o => o.TextContent));

        cut.Find("#run-search").Input(" main ");
        cut.Find("#run-status").Change(nameof(TestRunStatus.Partial));
        cut.Find("#run-environment").Change("docker compose tests");
        cut.Find("#run-from").Change("2026-10-01");
        cut.Find("#run-to").Change("2026-10-19");
        cut.Find("[data-testid=run-filters]").Submit();

        Assert.Equal(new TestRunQuery(1, 25, TestRunStatus.Partial, "docker compose tests", "main", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 19)), TestResults.RunQueries.Last());
        cut.WaitForAssertion(() => Assert.Contains("Try a different search or fewer filters.", cut.Find("[data-testid=runs-empty]").TextContent));
    }

    [Test]
    public void Runs_page_with_the_pager_and_show_each_runs_result()
    {
        for (var i = 1; i <= 27; i++)
        {
            TestResults.Runs.Add(FakeTestResultsService.Run(i, passed: 5, failed: i % 2, notExecuted: i == 2 ? 3 : 0, status: i == 4 ? TestRunStatus.FailedToStart : TestRunStatus.Completed));
        }
        var cut = Render<TestRuns>();
        cut.WaitForAssertion(() => Assert.Equal(25, cut.FindAll("[data-testid=runs-table] tbody tr").Count));
        var pills = cut.FindAll("[data-testid=runs-table] tbody tr .status-pill").Select(p => p.TextContent).ToList();
        Assert.Equal(["1 failed", "3 not run", "1 failed", "Failed to start"], pills.Take(4));
        Assert.Equal(25, cut.FindAll("[data-testid=run-cards] li").Count);

        cut.Find("[data-testid=pager-next]").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=runs-table] tbody tr").Count));
        cut.Find("[data-testid=pager-previous]").Click();
        cut.WaitForAssertion(() => Assert.Equal(25, cut.FindAll("[data-testid=runs-table] tbody tr").Count));
        Assert.Equal([1, 2, 1], TestResults.RunQueries.Select(q => q.Page));
    }

    [Test]
    public void Runs_list_errors_are_shown_in_the_alert()
    {
        TestResults.Failure = Unavailable;
        TestResults.Only = [nameof(ITestResultsService.ListRunsAsync)];
        var failed = Render<TestRuns>();
        failed.WaitForAssertion(() => Assert.Contains("unavailable", failed.Find("[data-testid=runs-error]").TextContent));

        TestResults.Failure = null;
        TestResults.Throw = new InvalidOperationException("boom");
        var thrown = Render<TestRuns>();
        thrown.WaitForAssertion(() => Assert.Contains("The test runs could not be loaded. Try again.", thrown.Find("[data-testid=runs-error]").TextContent));
        Assert.Empty(thrown.FindAll("[data-testid=runs-table]"));
    }

    [Test]
    public void Runs_list_shows_loading_until_the_service_answers()
    {
        TestResults.Gate = new TaskCompletionSource();
        TestResults.Only = [nameof(ITestResultsService.ListRunsAsync)];
        var cut = Render<TestRuns>();
        Assert.Contains("Loading test runs…", cut.Find("[data-testid=loading]").TextContent);
        Assert.Contains(cut.FindAll("#run-environment option"), o => o.TextContent == "docker compose tests");
        TestResults.Gate.SetResult();
        cut.WaitForElement("[data-testid=runs-empty]");
    }

    // ---- Run detail ----

    [Test]
    public void Run_detail_shows_suites_notes_comparison_and_run_files()
    {
        var run = FakeTestResultsService.Run(4, passed: 3, failed: 1, blocked: 1, notes: "Nightly run");
        var previous = FakeTestResultsService.Run(3, passed: 4, failed: 0);
        TestResults.Runs.AddRange([run, previous]);
        TestResults.Comparisons[4] = new TestRunComparisonDto(run, previous, [Row(9, "Started failing")], [Row(8, "Now passes", TestOutcome.Passed)], [], ["New.Test"], []);
        var result = FakeTestResultsService.Artifact(TestArtifactKind.ResultFile, "results.trx");
        TestResults.RunArtifacts.Add(result);
        TestResults.RunArtifacts.Add(FakeTestResultsService.Artifact(TestArtifactKind.ServerLog, "server.log", caption: "Web server log", purged: true));

        var cut = Render<RunDetail>(p => p.Add(r => r.RunId, 4));

        cut.WaitForElement("[data-testid=executions-empty]");
        Assert.Equal("Run run-4", cut.Find("h1").TextContent.Trim());
        Assert.Contains("Completed · 5 total, 3 passed, 1 failed, 0 skipped, 1 blocked, 0 not run", cut.Find("[data-testid=record-header]").TextContent);
        Assert.Contains("60%", cut.Find("[data-testid=record-header]").TextContent);
        Assert.Equal("/test-results/runs/4/export/html", cut.Find("[data-testid=export-html]").GetAttribute("href"));
        Assert.Equal("/test-results/runs/4/export/csv", cut.Find("[data-testid=export-csv]").GetAttribute("href"));
        Assert.Contains("No results", cut.Find("[data-testid=suites-table]").TextContent);
        Assert.Contains("Component (bUnit)", cut.Find("[data-testid=suites-table]").TextContent);
        Assert.Contains("Nightly run", cut.Markup);

        var comparison = cut.Find("[data-testid=run-comparison]");
        Assert.Equal("test-results/runs/3", comparison.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(["1", "1", "0", "1", "0"], comparison.QuerySelectorAll("dd").Select(d => d.TextContent));
        Assert.Equal("test-results/executions/9", comparison.QuerySelector("ul a")!.GetAttribute("href"));

        var files = cut.Find("[data-testid=run-files]");
        Assert.Equal($"/test-results/artifacts/{result.Id}?download=true", files.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Result file · 2.0 KB", files.TextContent);
        Assert.Contains("Web server log · 2.0 KB · removed by retention", files.TextContent);
        Assert.Single(files.QuerySelectorAll("a"));
    }

    [Test]
    public void Run_detail_filters_results_by_outcome_and_name_and_pages()
    {
        TestResults.Runs.Add(FakeTestResultsService.Run(4, passed: 60, failed: 1));
        TestResults.Executions.Add(Row(1, "The failure"));
        for (var i = 2; i <= 61; i++)
        {
            TestResults.Executions.Add(Row(i, $"Pass {i}", TestOutcome.Passed));
        }
        var cut = Render<RunDetail>(p => p.Add(r => r.RunId, 4));
        cut.WaitForAssertion(() => Assert.Equal(50, cut.FindAll("[data-testid=executions-table] tbody tr").Count));
        Assert.Contains("No earlier run to compare with.", cut.Find("[data-testid=run-comparison]").TextContent);

        cut.Find("[data-testid=pager-next]").Click();
        cut.WaitForAssertion(() => Assert.Equal(11, cut.FindAll("[data-testid=executions-table] tbody tr").Count));

        // Filters apply as soon as they change (the search after a short pause) and return to page 1.
        cut.Find("#execution-outcome-filter").Change(nameof(TestOutcome.Failed));
        cut.Find("#execution-search").Input(" failure ");

        cut.WaitForAssertion(() => Assert.Equal("failure", TestResults.ExecutionQueries.Last().Search), TimeSpan.FromSeconds(5));
        var query = TestResults.ExecutionQueries.Last();
        Assert.Equal(4, query.RunId);
        Assert.Equal(1, query.Page);
        Assert.Equal([TestOutcome.Failed], query.Outcomes);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=executions-table] tbody tr")));

        cut.Find("#execution-outcome-filter").Change("");
        cut.Find("#execution-search").Input("");
        cut.WaitForAssertion(() => Assert.Null(TestResults.ExecutionQueries.Last().Search), TimeSpan.FromSeconds(5));
        Assert.Null(TestResults.ExecutionQueries.Last().Outcomes);
    }

    [Test]
    public void Run_detail_errors_missing_runs_and_failed_result_lists_are_reported()
    {
        var missing = Render<RunDetail>(p => p.Add(r => r.RunId, 99));
        missing.WaitForAssertion(() => Assert.Contains("The test run was not found.", missing.Find("[data-testid=run-error]").TextContent));
        Assert.Equal("Test run", missing.Find("h1").TextContent.Trim());
        Assert.Empty(missing.FindAll("[data-testid=record-header]"));

        TestResults.Runs.Add(FakeTestResultsService.Run(4, passed: 1, failed: 0));
        TestResults.Failure = Unavailable;
        TestResults.Only = [nameof(ITestResultsService.ListExecutionsAsync)];
        var listFailed = Render<RunDetail>(p => p.Add(r => r.RunId, 4));
        listFailed.WaitForAssertion(() => Assert.Contains("unavailable", listFailed.Find("[data-testid=executions-error]").TextContent));
        Assert.Empty(listFailed.FindAll("[data-testid=executions-table]"));
        Assert.NotEmpty(listFailed.FindAll("[data-testid=record-header]"));

        TestResults.Failure = null;
        TestResults.Throw = new InvalidOperationException("boom");
        TestResults.Only = [nameof(ITestResultsService.CompareWithPreviousAsync)];
        var thrown = Render<RunDetail>(p => p.Add(r => r.RunId, 4));
        thrown.WaitForAssertion(() => Assert.Contains("The test run could not be loaded. Try again.", thrown.Find("[data-testid=run-error]").TextContent));
    }

    [Test]
    public void Run_detail_shows_loading_until_the_run_arrives()
    {
        TestResults.Runs.Add(FakeTestResultsService.Run(4, passed: 1, failed: 0));
        TestResults.Gate = new TaskCompletionSource();
        var cut = Render<RunDetail>(p => p.Add(r => r.RunId, 4));
        Assert.Contains("Loading test run…", cut.Find("[data-testid=loading]").TextContent);
        TestResults.Gate.SetResult();
        cut.WaitForElement("[data-testid=record-header]");
    }

    // ---- Execution detail ----

    [Test]
    public void Passing_execution_without_evidence_or_logs_says_what_was_not_recorded()
    {
        var run = FakeTestResultsService.Run(3, passed: 1, failed: 0);
        TestResults.Details[8] = new TestExecutionDetailDto(8,
            FakeTestResultsService.TestCase(12, "Unit check", story: null) with { Type = TestType.Unit, Description = null, Priority = null, LastOutcome = null },
            run, TestOutcome.Passed, new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 3, 9, 0, 1, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(20), null, null, null, null, null, null, null, null, [], [], []);

        var cut = Render<ExecutionDetail>(p => p.Add(d => d.Id, 8));

        cut.WaitForAssertion(() => Assert.Equal("Passed", cut.Find("[data-testid=execution-outcome]").TextContent));
        Assert.Contains("No evidence files were recorded", cut.Find("[data-testid=no-evidence]").TextContent);
        Assert.Contains("Not a browser test", cut.Markup);
        Assert.Contains("10-03-2026 05:00 ET", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid=failure-message]"));
        Assert.Empty(cut.FindAll("[data-testid=steps-table]"));
        Assert.Empty(cut.FindAll("#logs-heading"));
        Assert.Contains(new Crumb("Run run-3", "test-results/runs/3"), Services.GetRequiredService<ShellState>().Breadcrumbs);
    }

    [Test]
    public void Execution_detail_shows_logs_stack_trace_and_purged_screenshots()
    {
        var run = FakeTestResultsService.Run(3, passed: 0, failed: 1);
        var purged = FakeTestResultsService.Artifact(TestArtifactKind.Screenshot, "gone.png", purged: true);
        TestResults.Details[9] = new TestExecutionDetailDto(9, FakeTestResultsService.TestCase(13, "Logs"), run, TestOutcome.Failed, null, null,
            TimeSpan.FromSeconds(75), "chromium", "390x844", null, "Boom", "at Line(1)", "stdout line", null, "GET /x 500",
            [new TestStepDto(1, "Open", TestOutcome.Passed, TimeSpan.FromMilliseconds(5), null, null, "ok")], [purged], ["R-1"]);

        var cut = Render<ExecutionDetail>(p => p.Add(d => d.Id, 9));

        cut.WaitForElement("[data-testid=network-log]");
        Assert.Equal("at Line(1)", cut.Find("[data-testid=stack-trace]").TextContent);
        Assert.Equal("stdout line", cut.Find("[data-testid=standard-output]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=console-log]"));
        Assert.Contains("Open (ok)", cut.Find("[data-testid=steps-table]").TextContent);
        Assert.Contains("1 min 15 s", cut.Markup);
        Assert.Contains("chromium, 390x844", cut.Markup);
        Assert.Contains("Removed by the retention policy.", cut.Find("[data-testid=screenshots]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=screenshots] button"));
        Assert.Empty(cut.FindAll("[data-testid=evidence-files] a"));
        Assert.Contains("removed by retention", cut.Find("[data-testid=evidence-files]").TextContent);
    }

    [Test]
    public void Execution_detail_errors_and_loading()
    {
        var missing = Render<ExecutionDetail>(p => p.Add(d => d.Id, 99));
        missing.WaitForAssertion(() => Assert.Contains("The test result was not found.", missing.Find("[data-testid=execution-error]").TextContent));
        Assert.Equal("Test result", missing.Find("h1").TextContent.Trim());

        TestResults.Details[1] = new TestExecutionDetailDto(1, FakeTestResultsService.TestCase(1, "Any"), FakeTestResultsService.Run(1, 1, 0), TestOutcome.Passed,
            null, null, TimeSpan.Zero, null, null, null, null, null, null, null, null, [], [], []);
        TestResults.Throw = new InvalidOperationException("boom");
        var thrown = Render<ExecutionDetail>(p => p.Add(d => d.Id, 1));
        thrown.WaitForAssertion(() => Assert.Contains("The test result could not be loaded. Try again.", thrown.Find("[data-testid=execution-error]").TextContent));

        TestResults.Throw = null;
        TestResults.Gate = new TaskCompletionSource();
        var slow = Render<ExecutionDetail>(p => p.Add(d => d.Id, 1));
        Assert.Contains("Loading test result…", slow.Find("[data-testid=loading]").TextContent);
        TestResults.Gate.SetResult();
        slow.WaitForElement("[data-testid=execution-outcome]");
    }

    // ---- Dashboard ----

    [Test]
    public void Dashboard_lists_newly_failed_and_fixed_tests_recent_runs_and_exports()
    {
        var latest = FakeTestResultsService.Run(2, passed: 40, failed: 1);
        var previous = FakeTestResultsService.Run(1, passed: 41, failed: 0, notExecuted: 2);
        TestResults.Dashboard = new TestDashboardDto(latest,
            new TestRunComparisonDto(latest, previous, [Row(5, "Now failing")], [Row(6, "Now fixed", TestOutcome.Passed)], [], [], []),
            [latest, previous], [], [], [new LabelCount("Unit", 41)], 41);

        var cut = Render<TestResultsDashboard>();

        cut.WaitForElement("[data-testid=tr-comparison]");
        var comparison = cut.Find("[data-testid=tr-comparison]");
        Assert.Contains("Compared with run run-1.", comparison.TextContent);
        Assert.Contains("Newly failed (1)", comparison.TextContent);
        Assert.Contains("Newly fixed (1)", comparison.TextContent);
        Assert.Equal(["test-results/executions/5", "test-results/executions/6"], comparison.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal("1", cut.Find("[data-testid=tr-kpi-newly-failed] .kpi__value").TextContent);
        Assert.Equal("/test-results/runs/2/export/html", cut.Find("[data-testid=export-latest-html]").GetAttribute("href"));
        Assert.Equal("/test-results/runs/2/export/csv", cut.Find("[data-testid=export-latest-csv]").GetAttribute("href"));
        Assert.Contains("No failures in the latest run.", cut.Find("[data-testid=tr-chart-modules]").TextContent);
        var recent = cut.FindAll("[data-testid=tr-recent] li");
        Assert.Equal(2, recent.Count);
        Assert.Contains("main @ abcdef1234", recent[0].TextContent);
        Assert.Equal("2 not run", recent[1].QuerySelector(".status-pill")!.TextContent);
    }

    [Test]
    public void Dashboard_without_a_previous_run_or_changes_says_so()
    {
        var only = FakeTestResultsService.Run(1, passed: 5, failed: 0);
        TestResults.Dashboard = new TestDashboardDto(only, new TestRunComparisonDto(only, null, [], [], [], [], []), [only], [], [], [], 5);
        var first = Render<TestResultsDashboard>();
        first.WaitForAssertion(() => Assert.Contains("nothing to compare with", first.Find("[data-testid=tr-comparison]").TextContent));
        Assert.Equal("Passed", first.Find("[data-testid=tr-kpi-status] .kpi__value").TextContent);

        var previous = FakeTestResultsService.Run(2, passed: 5, failed: 0);
        TestResults.Dashboard = new TestDashboardDto(only, new TestRunComparisonDto(only, previous, [], [], [], [], []), [only, previous], [], [], [], 5);
        var unchanged = Render<TestResultsDashboard>();
        unchanged.WaitForAssertion(() => Assert.Contains("No test started failing.", unchanged.Find("[data-testid=tr-comparison]").TextContent));
        Assert.Contains("No test was fixed.", unchanged.Find("[data-testid=tr-comparison]").TextContent);
    }

    [Test]
    public void Dashboard_load_failures_show_an_alert_with_a_title()
    {
        TestResults.Failure = Unavailable;
        var failed = Render<TestResultsDashboard>();
        failed.WaitForAssertion(() => Assert.Contains("Test results could not be loaded", failed.Find("[data-testid=tr-error]").TextContent));
        Assert.Contains("unavailable", failed.Find("[data-testid=tr-error]").TextContent);
        Assert.Empty(failed.FindAll("[data-testid=tr-empty]"));

        TestResults.Failure = null;
        TestResults.Throw = new InvalidOperationException("boom");
        var thrown = Render<TestResultsDashboard>();
        thrown.WaitForAssertion(() => Assert.Contains("The test results could not be loaded. Try again.", thrown.Find("[data-testid=tr-error]").TextContent));

        TestResults.Throw = null;
        thrown.Find("[data-testid=tr-refresh]").Click();
        thrown.WaitForElement("[data-testid=tr-empty]");
        Assert.Empty(thrown.Find("[data-testid=tr-error]").TextContent.Trim());
    }

    [Test]
    public void Import_reports_problems_nothing_new_and_failures()
    {
        TestResults.ImportResult = new ImportSummaryDto(0, 2, 0, 0, [.. Enumerable.Range(1, 12).Select(i => $"bad-{i}.trx")]);
        var cut = Render<TestResultsDashboard>();
        cut.WaitForElement("[data-testid=import-runs]").Click();

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll("[data-testid=import-problems] li").Count));
        Assert.Equal("No new runs to import; 0 run(s) imported.", Announcer.Message);
        Assert.Equal("Import new runs", cut.Find("[data-testid=import-runs]").TextContent.Trim());

        TestResults.Failure = new Error(ErrorKind.Forbidden, "Forbidden", "You are not allowed to import test runs.");
        cut.Find("[data-testid=import-runs]").Click();
        cut.WaitForAssertion(() => Assert.Contains("You are not allowed to import test runs.", cut.Find("[data-testid=tr-error]").TextContent));
        Assert.Equal(2, TestResults.ImportCalls);
    }

    [Test]
    public void Dashboard_shows_loading_and_disables_import_while_importing()
    {
        TestResults.Gate = new TaskCompletionSource();
        var cut = Render<TestResultsDashboard>();
        Assert.Contains("Loading test results…", cut.Find("[data-testid=loading]").TextContent);
        Assert.True(cut.Find("[data-testid=tr-refresh]").HasAttribute("disabled"));
        TestResults.Gate.SetResult();
        cut.WaitForElement("[data-testid=tr-empty]");

        TestResults.Gate = new TaskCompletionSource();
        cut.Find("[data-testid=import-runs]").Click();
        Assert.Equal("Importing…", cut.Find("[data-testid=import-runs]").TextContent.Trim());
        Assert.Equal("true", cut.Find("[data-testid=import-runs]").GetAttribute("aria-busy"));
        TestResults.Gate.SetResult();
        cut.WaitForAssertion(() => Assert.Equal("Import new runs", cut.Find("[data-testid=import-runs]").TextContent.Trim()));
    }

    // ---- Display text ----
    [TestCase(TestArtifactKind.ConsoleLog, "Console log")]
    [TestCase(TestArtifactKind.NetworkLog, "Network log")]
    [TestCase(TestArtifactKind.ResultFile, "Result file")]
    [TestCase(TestArtifactKind.Video, "Video")]
    public void Artifact_kinds_read_as_words(TestArtifactKind kind, string text) => Assert.Equal(text, TestResultsText.Kind(kind));

    [Test]
    public void Run_results_types_suites_and_commits_have_display_text()
    {
        Assert.Equal(("2 blocked", "outcome-blocked"), TestResultsText.RunResult(FakeTestResultsService.Run(1, passed: 1, failed: 0, blocked: 2)));
        Assert.Equal("Failed to start", TestResultsText.RunStatus(TestRunStatus.FailedToStart));
        Assert.Equal("Failed to start", TestResultsText.SuiteStatus("FailedToStart"));
        Assert.Equal("Completed", TestResultsText.SuiteStatus("Completed"));
        Assert.Equal("Integration", TestResultsText.Type(TestType.Integration));
        Assert.Equal("Unit", TestResultsText.Type(TestType.Unit));
        Assert.Equal("99", TestResultsText.Type((TestType)99));
        Assert.Equal("abc", TestResultsText.ShortCommit("abc"));
        Assert.Equal("n/a", TestResultsText.PassRate(new TestOutcomeCounts(2, 0, 0, 1, 0, 1)));
        Assert.Equal("outcome-notexecuted", TestResultsText.OutcomeCss(TestOutcome.NotExecuted));
    }
}
