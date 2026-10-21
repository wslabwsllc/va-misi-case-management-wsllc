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

    /// <summary>"Not Run" for tests that never executed (never shown as passed); "Not Applicable" spelled out.</summary>
    public static string Outcome(TestOutcome outcome) => outcome switch
    {
        TestOutcome.NotExecuted => "Not Run",
        TestOutcome.NotApplicable => "Not Applicable",
        _ => outcome.ToString(),
    };

    public static string Classification(AccessibilityClassification? classification) => classification switch
    {
        AccessibilityClassification.Section508 => "Section 508",
        AccessibilityClassification.Wcag22Project => "WCAG 2.2 AA project check",
        _ => "Not classified",
    };

    public static string Method(TestMethod? method) => method switch
    {
        TestMethod.Automated => "Automated",
        TestMethod.ManualKeyboard => "Manual keyboard",
        TestMethod.ScreenReader => "Screen reader",
        TestMethod.VisualOther => "Visual or other",
        _ => "Not recorded",
    };

    /// <summary>"WCAG 2.2 · 2.4.11 · AA", or null without a mapping.</summary>
    public static string? Wcag(string? version, string? criterion, string? level) =>
        version is null && criterion is null ? null : string.Join(" · ", new[] { version is null ? null : $"WCAG {version}", criterion, level }.OfType<string>());

    /// <summary>Passed ÷ (Passed + Failed), "N/A" when neither occurred. Not a conformance score.</summary>
    public static string VerdictPassRate(TestOutcomeCounts c) => c.VerdictPassRate is { } rate ? UiText.Percent(rate) : "N/A";

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
        TestType.Manual => "Manual",
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
        { Counts.NotExecuted: > 0 } => (Count(run.Counts.NotExecuted, "not run"), "outcome-notexecuted"),
        { Counts.Blocked: > 0 } => (Count(run.Counts.Blocked, "blocked"), "outcome-blocked"),
        _ => ("Passed", "outcome-passed"),
    };

    public static string Counts(TestOutcomeCounts c) => string.Create(CultureInfo.InvariantCulture,
        $"{c.Total} total, {c.Passed} passed, {c.Failed} failed, {c.Skipped} skipped, {c.Blocked} blocked, {c.NotExecuted} not run{(c.NotApplicable > 0 ? $", {c.NotApplicable} not applicable" : "")}");

    public static string PassRate(TestOutcomeCounts c) => c.PassRate is { } rate ? UiText.Percent(rate) : "n/a";

    public static string ShortCommit(string? commit) => commit is null ? "unknown" : commit.Length > 10 ? commit[..10] : commit;

    /// <summary>A short run label for chart axes: the Eastern start, such as "10-21 09:05 ET".</summary>
    public static string ChartLabel(TestRunSummaryDto run) =>
        UiText.ShortTime(run.StartedAtUtc);

    public static string RunHref(long runId) => $"test-results/runs/{runId}";

    public static string ExecutionHref(long executionId) => $"test-results/executions/{executionId}";

    public static string TestCaseHref(long testCaseId) => $"test-results/cases/{testCaseId}";

    private static string Count(int count, string what) => string.Create(CultureInfo.InvariantCulture, $"{count} {what}");
}
