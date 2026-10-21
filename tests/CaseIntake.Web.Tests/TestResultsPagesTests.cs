using System.Reflection;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.Web.Components.Pages.TestResults;
using Microsoft.AspNetCore.Authorization;

namespace CaseIntake.Web.Tests;

/// <summary>Test results pages: protected by testresults.view, metrics from service data, outcomes kept distinct.</summary>
[Category("Story:Test management")]
[Category("Req:R-4.2-13")]
public class TestResultsPagesTests : WebTestContext
{
    public TestResultsPagesTests() => SignInAs(AppRoles.QualityAnalyst);

    public static TheoryData<Type> Pages => [typeof(TestResultsDashboard), typeof(TestRuns), typeof(RunDetail), typeof(ExecutionDetail), typeof(TestCases), typeof(TestCaseDetail), typeof(Evidence), typeof(History)];

    [TestCaseSource(nameof(Pages))]
    public void Every_page_requires_the_test_results_view_policy(Type page)
    {
        var policy = Assert.Single(page.GetCustomAttributes<AuthorizeAttribute>()).Policy;
        Assert.Equal(Permissions.TestResultsView, policy);
    }

    [Test]
    public void Dashboard_without_runs_explains_how_to_produce_them()
    {
        var cut = Render<TestResultsDashboard>();
        cut.WaitForAssertion(() => Assert.Contains("No test runs imported yet", cut.Find("[data-testid=tr-empty]").TextContent));
        Assert.Contains("scripts/run-all-tests.sh", cut.Find("[data-testid=tr-empty]").TextContent);
    }

    [Test]
    public void Dashboard_shows_metrics_of_the_latest_run_and_distinguishes_not_executed()
    {
        var latest = FakeTestResultsService.Run(2, passed: 40, failed: 2, skipped: 1, notExecuted: 5, status: TestRunStatus.Partial);
        var previous = FakeTestResultsService.Run(1, passed: 45, failed: 0);
        TestResults.Dashboard = new TestDashboardDto(latest, new TestRunComparisonDto(latest, previous, [], [], [], [], []),
            [latest, previous], [new LabelCount("Web.Tests", 2)], [new LabelCount("chromium 1440x900", 7)], [new LabelCount("Unit", 48)], 60);

        var cut = Render<TestResultsDashboard>();

        cut.WaitForAssertion(() => Assert.Equal("48", cut.Find("[data-testid=tr-kpi-total] .kpi__value").TextContent));
        Assert.Equal("40", cut.Find("[data-testid=tr-kpi-passed] .kpi__value").TextContent);
        Assert.Equal("2", cut.Find("[data-testid=tr-kpi-failed] .kpi__value").TextContent);
        Assert.Equal("1", cut.Find("[data-testid=tr-kpi-skipped] .kpi__value").TextContent);
        Assert.Equal("5", cut.Find("[data-testid=tr-kpi-notexecuted] .kpi__value").TextContent);
        Assert.Equal("95.2%", cut.Find("[data-testid=tr-kpi-passrate] .kpi__value").TextContent);
        Assert.Equal("2 failed", cut.Find("[data-testid=tr-kpi-status] .kpi__value").TextContent);
        Assert.Contains("Partial", cut.Find("[data-testid=tr-kpi-status] .kpi__hint").TextContent);
        Assert.Equal("img", cut.Find("[data-testid=tr-chart-trend] svg").GetAttribute("role"));
        Assert.Contains("chromium 1440x900", cut.Find("[data-testid=tr-chart-coverage] desc").TextContent);
    }

    [Test]
    public void Import_is_offered_only_with_the_manage_permission()
    {
        var qa = Render<TestResultsDashboard>();
        qa.WaitForElement("[data-testid=import-runs]").Click();
        Assert.Equal(1, TestResults.ImportCalls);
        Assert.Contains("1 run(s) imported", Announcer.Message);

        Auth.SetPolicies(Permissions.TestResultsView);
        var viewer = Render<TestResultsDashboard>();
        viewer.WaitForAssertion(() => Assert.NotEmpty(viewer.FindAll("[data-testid=tr-refresh]")));
        Assert.Empty(viewer.FindAll("[data-testid=import-runs]"));
    }

    [Test]
    public void Execution_detail_shows_failure_expected_actual_steps_and_screenshot_preview_controls()
    {
        var run = FakeTestResultsService.Run(3, passed: 1, failed: 1);
        var shot = new TestArtifactDto(Guid.NewGuid(), TestArtifactKind.Screenshot, "01-failure.png", "image/png", 2048, DateTimeOffset.UnixEpoch, false, "Failure: Confirm");
        var trace = new TestArtifactDto(Guid.NewGuid(), TestArtifactKind.Trace, "trace.zip", "application/zip", 4096, DateTimeOffset.UnixEpoch, false, null);
        TestResults.Details[7] = new TestExecutionDetailDto(7,
            new TestCaseDto(11, "Ns.Class.Method", "Confirm the transition", "E2E", TestType.EndToEnd, "Desc", "E5-S6", "R-4.2-8", null, "High", "A New case", "Status Triaged", TestOutcome.Failed, 3, DateTimeOffset.UnixEpoch),
            run, TestOutcome.Failed, null, null, TimeSpan.FromSeconds(3), "chromium 141", "1440x900", "Status stayed New",
            "Expected Triaged but was New", "at Line(1)", null, "console.error: x", null,
            [new TestStepDto(1, "Confirm the transition", TestOutcome.Failed, TimeSpan.FromMilliseconds(80), "Status: Triaged", "Status: New", null)],
            [shot, trace], ["R-4.2-8"]);

        var cut = Render<ExecutionDetail>(p => p.Add(d => d.Id, 7));

        cut.WaitForAssertion(() => Assert.Equal("Failed", cut.Find("[data-testid=execution-outcome]").TextContent));
        Assert.Contains("Expected Triaged but was New", cut.Find("[data-testid=failure-message]").TextContent);
        Assert.Contains("Status stayed New", cut.Find("[data-testid=actual-result]").TextContent);
        Assert.Contains("Status: Triaged", cut.Find("[data-testid=steps-table]").TextContent);
        Assert.Equal($"/test-results/artifacts/{shot.Id}", cut.Find("[data-testid=screenshots] img").GetAttribute("src"));
        Assert.Equal("Preview screenshot: Failure: Confirm", cut.Find("[data-testid=screenshots] button").GetAttribute("aria-label"));
        Assert.Contains(cut.FindAll("[data-testid=evidence-files] a"), a => a.GetAttribute("href") == $"/test-results/artifacts/{trace.Id}?download=true");

        cut.Find("[data-testid=screenshots] button").Click();
        InteropModule.VerifyInvoke("showDialog");
        Assert.Contains("Zoom in", cut.Find("[data-testid=evidence-preview]").InnerHtml);
    }

    [Test]
    public void Run_list_and_failure_messages_come_from_the_service()
    {
        TestResults.Runs.Add(FakeTestResultsService.Run(4, passed: 3, failed: 1));
        TestResults.Executions.Add(new TestExecutionRowDto(1, 4, 1, "Shows a failure", "Ns.C.Shows_a_failure", "E2E", TestType.EndToEnd, TestOutcome.Failed, TimeSpan.FromSeconds(2), "chromium", "390x844", "Boom", 2));

        var runs = Render<TestRuns>();
        runs.WaitForAssertion(() => Assert.Contains("run-4", runs.Find("[data-testid=runs-table]").TextContent));

        var detail = Render<RunDetail>(p => p.Add(r => r.RunId, 4));
        detail.WaitForAssertion(() => Assert.Contains("Shows a failure", detail.Find("[data-testid=executions-table]").TextContent));
        Assert.Contains("Boom", detail.Find("[data-testid=executions-table]").TextContent);
        Assert.Contains("1 failed", detail.Find("[data-testid=record-header]").TextContent);
    }

    [TestCase(TestOutcome.NotExecuted, "Not Run")]
    [TestCase(TestOutcome.NotApplicable, "Not Applicable")]
    [TestCase(TestOutcome.Blocked, "Blocked")]
    [TestCase(TestOutcome.Skipped, "Skipped")]
    public void Outcomes_are_labelled_distinctly(TestOutcome outcome, string label)
    {
        var cut = Render<OutcomePill>(p => p.Add(o => o.Outcome, outcome));
        Assert.Equal(label, cut.Find(".status-pill").TextContent);
        Assert.Contains($"outcome-{outcome.ToString().ToLowerInvariant()}", cut.Find(".status-pill").ClassList);
    }
}
