using System.Globalization;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.Web.Services;

namespace CaseIntake.Web.Components.Pages.TestResults;

/// <summary>Display text for the test results pages, so an outcome or a run result reads the same everywhere.</summary>
public static class TestResultsText
{
    /// <summary>The empty-state instruction shown until the first run is imported.</summary>
    public const string HowToProduceRuns =
        "Run the documented test command (scripts/run-all-tests.sh, or docker compose run --rm tests). New runs are imported automatically within a minute, or with Import new runs.";

    public static string Outcome(TestOutcome outcome) => outcome == TestOutcome.NotExecuted ? "Not executed" : outcome.ToString();

    public static string OutcomeCss(TestOutcome outcome) => "outcome-" + outcome.ToString().ToLowerInvariant();

    public static string RunStatus(TestRunStatus status) => status switch
    {
        TestRunStatus.FailedToStart => "Failed to start",
        _ => status.ToString(),
    };

    /// <summary>Suite statuses as written by scripts/run-tests.sh (Completed, NoResults, FailedToStart).</summary>
    public static string SuiteStatus(string status) => status switch
    {
        "FailedToStart" => "Failed to start",
        "NoResults" => "No results",
        _ => status,
    };

    public static string Type(TestType type) => type switch
    {
        TestType.Unit => "Unit",
        TestType.Component => "Component (bUnit)",
        TestType.Integration => "Integration",
        TestType.EndToEnd => "End-to-end (Playwright)",
        _ => type.ToString(),
    };

    public static string Kind(TestArtifactKind kind) => kind switch
    {
        TestArtifactKind.ConsoleLog => "Console log",
        TestArtifactKind.NetworkLog => "Network log",
        TestArtifactKind.ResultFile => "Result file",
        TestArtifactKind.ServerLog => "Server log",
        _ => kind.ToString(),
    };

    /// <summary>The headline result of a run (text and pill class): failures first, then tests that did not run.</summary>
    public static (string Text, string Css) RunResult(TestRunSummaryDto run) => run switch
    {
        { Status: TestRunStatus.FailedToStart } => ("Failed to start", "outcome-failed"),
        { Counts.Failed: > 0 } => (Count(run.Counts.Failed, "failed"), "outcome-blocked"),
        { Counts.NotExecuted: > 0 } => (Count(run.Counts.NotExecuted, "not executed"), "outcome-notexecuted"),
        { Counts.Blocked: > 0 } => (Count(run.Counts.Blocked, "blocked"), "outcome-blocked"),
        _ => ("Passed", "outcome-passed"),
    };

    public static string Counts(TestOutcomeCounts c) => string.Create(CultureInfo.InvariantCulture,
        $"{c.Total} total, {c.Passed} passed, {c.Failed} failed, {c.Skipped} skipped, {c.Blocked} blocked, {c.NotExecuted} not executed");

    public static string PassRate(TestOutcomeCounts c) => c.PassRate is { } rate ? UiText.Percent(rate) : "n/a";

    public static string ShortCommit(string? commit) => commit is null ? "unknown" : commit.Length > 10 ? commit[..10] : commit;

    /// <summary>A short run label for chart axes: the Eastern start as MM-dd HH:mm.</summary>
    public static string ChartLabel(TestRunSummaryDto run) =>
        Domain.EasternTime.From(run.StartedAtUtc).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string RunHref(long runId) => $"test-results/runs/{runId}";

    public static string ExecutionHref(long executionId) => $"test-results/executions/{executionId}";

    public static string TestCaseHref(long testCaseId) => $"test-results/cases/{testCaseId}";

    private static string Count(int count, string what) => string.Create(CultureInfo.InvariantCulture, $"{count} {what}");
}
