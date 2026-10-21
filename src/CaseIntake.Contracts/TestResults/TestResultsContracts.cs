using CaseIntake.Contracts.Common;
using CaseIntake.Domain.TestResults;

namespace CaseIntake.Contracts.TestResults;

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>A label and a count (chart and breakdown rows).</summary>
public sealed record LabelCount(string Label, int Count);

public sealed record TestOutcomeCounts(int Total, int Passed, int Failed, int Skipped, int Blocked, int NotExecuted, int NotApplicable = 0)
{
    /// <summary>
    /// Passed / (Passed + Failed), 0 to 1, or null when neither occurred (shown as "N/A"). Used by the accessibility
    /// summary; it is a share of executed verdicts, never a conformance score.
    /// </summary>
    public double? VerdictPassRate => Passed + Failed == 0 ? null : Passed / (double)(Passed + Failed);

    /// <summary>Tests that ran to a verdict: passed, failed or blocked (skipped and not executed are excluded).</summary>
    public int Executed => Passed + Failed + Blocked;

    /// <summary>Passed share of the executed tests (0 to 1), or null when nothing executed.</summary>
    public double? PassRate => Executed == 0 ? null : Passed / (double)Executed;
}

public sealed record TestRunSummaryDto(long Id, string RunKey, TestRunStatus Status, DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, TimeSpan Duration, string Environment, string? GitBranch, string? GitCommit,
    string? BuildId, string? ImageVersion, string Runner, TestOutcomeCounts Counts, DateTimeOffset ImportedAtUtc, string? Notes);

public sealed record TestSuiteDto(string Name, TestType Type, string Status, int Tests, string? Message);

public sealed record TestArtifactDto(Guid Id, TestArtifactKind Kind, string FileName, string ContentType, long SizeBytes,
    DateTimeOffset CreatedAtUtc, bool Purged, string? Caption);

public sealed record TestRunDetailDto(TestRunSummaryDto Run, string OperatingSystem, string Runtime, string? BaseUrl,
    IReadOnlyList<TestSuiteDto> Suites, IReadOnlyList<TestArtifactDto> Artifacts);

public sealed record TestExecutionRowDto(long Id, long RunId, long TestCaseId, string TestTitle, string FullName, string Module,
    TestType Type, TestOutcome Outcome, TimeSpan Duration, string? Browser, string? Viewport, string? FailureMessage, int ArtifactCount,
    AccessibilityClassification? Accessibility = null, string? Section508Provision = null, string? WcagVersion = null,
    string? WcagCriterion = null, string? WcagLevel = null, TestMethod? Method = null, DateTimeOffset? ExecutedAtUtc = null,
    string? Tester = null, string? AssistiveTechnology = null, string? DefectReference = null, string? Workflow = null);

/// <summary>A run against the run before it (by start time). Previous is null for the first imported run.</summary>
public sealed record TestRunComparisonDto(TestRunSummaryDto Current, TestRunSummaryDto? Previous,
    IReadOnlyList<TestExecutionRowDto> NewlyFailed, IReadOnlyList<TestExecutionRowDto> NewlyFixed,
    IReadOnlyList<TestExecutionRowDto> StillFailing, IReadOnlyList<string> NewTests, IReadOnlyList<string> RemovedTests);

/// <summary>The test results dashboard: the latest run, its comparison and breakdowns, and recent runs (newest first).</summary>
public sealed record TestDashboardDto(TestRunSummaryDto? Latest, TestRunComparisonDto? Comparison,
    IReadOnlyList<TestRunSummaryDto> RecentRuns, IReadOnlyList<LabelCount> FailuresByModule,
    IReadOnlyList<LabelCount> BrowserCoverage, IReadOnlyList<LabelCount> TestsByType, int KnownTestCases);

public sealed record TestCaseDto(long Id, string FullName, string Title, string Module, TestType Type, string? Description,
    string? Story, string? Requirements, string? AcceptanceCriteria, string? Priority, string? Preconditions,
    string? ExpectedResult, TestOutcome? LastOutcome, long? LastRunId, DateTimeOffset FirstSeenAtUtc,
    AccessibilityClassification? Accessibility = null, string? Section508Provision = null, string? WcagVersion = null,
    string? WcagCriterion = null, string? WcagLevel = null, TestMethod? Method = null, string? Workflow = null);

public sealed record TestStepDto(int Order, string Name, TestOutcome Outcome, TimeSpan Duration, string? Expected, string? Actual, string? Message);

public sealed record TestExecutionDetailDto(long Id, TestCaseDto TestCase, TestRunSummaryDto Run, TestOutcome Outcome,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? FinishedAtUtc, TimeSpan Duration, string? Browser, string? Viewport,
    string? ActualResult, string? ErrorMessage, string? StackTrace, string? StandardOutput, string? ConsoleLog, string? NetworkLog,
    IReadOnlyList<TestStepDto> Steps, IReadOnlyList<TestArtifactDto> Artifacts, IReadOnlyList<string> Requirements,
    string? Tester = null, string? AssistiveTechnology = null, string? DefectReference = null, string? RemediationNotes = null);

public sealed record TestHistoryPointDto(long RunId, string RunKey, DateTimeOffset RunStartedAtUtc, TestOutcome Outcome,
    TimeSpan Duration, string? GitCommit, long ExecutionId);

/// <summary>A test case with its executions in recent runs, newest first.</summary>
public sealed record TestCaseHistoryDto(TestCaseDto TestCase, IReadOnlyList<TestHistoryPointDto> History);

/// <summary>A test that both passed and failed recently (flaky) or failed in two or more recent runs (recurring).</summary>
public sealed record FlakyTestDto(long TestCaseId, string Title, string Module, int Runs, int Passed, int Failed, TestOutcome LastOutcome);

public sealed record TestArtifactListItemDto(TestArtifactDto Artifact, long RunId, string RunKey, long? ExecutionId,
    string? TestTitle, TestOutcome? Outcome);

/// <summary>An open evidence file. The caller owns <see cref="Content"/> and must dispose it.</summary>
public sealed record TestArtifactContent(Stream Content, string FileName, string ContentType, long SizeBytes, TestArtifactKind Kind);

public sealed record TestReportFile(byte[] Content, string FileName, string ContentType);

public sealed record ImportSummaryDto(int Imported, int Skipped, int Executions, int Artifacts, IReadOnlyList<string> Problems);

/// <summary>Run list filters. Dates are US Eastern calendar days (by run start), inclusive.</summary>
public sealed record TestRunQuery(int Page = 1, int PageSize = 25, TestRunStatus? Status = null, string? Environment = null,
    string? Search = null, DateOnly? FromDate = null, DateOnly? ToDate = null);

public sealed record TestExecutionQuery(long? RunId = null, IReadOnlyList<TestOutcome>? Outcomes = null, string? Module = null,
    TestType? Type = null, string? Search = null, int Page = 1, int PageSize = 50, Section508Filter Section508 = Section508Filter.All);

public sealed record TestCaseQuery(string? Module = null, TestType? Type = null, TestOutcome? LastOutcome = null,
    string? Search = null, int Page = 1, int PageSize = 50, Section508Filter Section508 = Section508Filter.All);

public sealed record TestArtifactQuery(long? RunId = null, TestArtifactKind? Kind = null, string? Search = null,
    int Page = 1, int PageSize = 48);

/// <summary>
/// Test results and evidence (E5 test management). Every call requires <c>testresults.view</c>; <see cref="ImportAsync"/>
/// requires <c>testresults.manage</c>. Evidence access and exports are audited.
/// </summary>
public interface ITestResultsService
{
    Task<Result<TestDashboardDto>> GetDashboardAsync(CancellationToken ct);
    Task<Result<PagedList<TestRunSummaryDto>>> ListRunsAsync(TestRunQuery query, CancellationToken ct);
    Task<Result<TestRunDetailDto>> GetRunAsync(long runId, CancellationToken ct);
    Task<Result<TestRunComparisonDto>> CompareWithPreviousAsync(long runId, CancellationToken ct);
    Task<Result<PagedList<TestExecutionRowDto>>> ListExecutionsAsync(TestExecutionQuery query, CancellationToken ct);
    Task<Result<TestExecutionDetailDto>> GetExecutionAsync(long executionId, CancellationToken ct);
    Task<Result<PagedList<TestCaseDto>>> ListTestCasesAsync(TestCaseQuery query, CancellationToken ct);
    Task<Result<TestCaseHistoryDto>> GetTestCaseHistoryAsync(long testCaseId, int maxRuns, CancellationToken ct);
    Task<Result<IReadOnlyList<FlakyTestDto>>> GetFlakyTestsAsync(int lastRuns, CancellationToken ct);
    Task<Result<PagedList<TestArtifactListItemDto>>> ListArtifactsAsync(TestArtifactQuery query, CancellationToken ct);
    Task<Result<IReadOnlyList<string>>> ListModulesAsync(CancellationToken ct);
    Task<Result<IReadOnlyList<string>>> ListEnvironmentsAsync(CancellationToken ct);
    Task<Result<TestArtifactContent>> OpenArtifactAsync(Guid artifactId, bool download, CancellationToken ct);

    /// <param name="format"><c>csv</c> or <c>html</c>.</param>
    Task<Result<TestReportFile>> ExportRunAsync(long runId, string format, CancellationToken ct);
    Task<Result<ImportSummaryDto>> ImportAsync(CancellationToken ct);

    /// <summary>Outcome counts over every execution matching <paramref name="query"/> (paging ignored).</summary>
    Task<Result<TestOutcomeCounts>> SummarizeExecutionsAsync(TestExecutionQuery query, CancellationToken ct);

    /// <summary>Last-outcome counts over every test case matching <paramref name="query"/> (paging ignored; never run = Not Run).</summary>
    Task<Result<TestOutcomeCounts>> SummarizeTestCasesAsync(TestCaseQuery query, CancellationToken ct);

    /// <summary>CSV of every execution matching <paramref name="query"/>, with the accessibility fields (audited).</summary>
    Task<Result<TestReportFile>> ExportExecutionsAsync(TestExecutionQuery query, CancellationToken ct);

    /// <summary>CSV of every test case matching <paramref name="query"/>, with the accessibility fields (audited).</summary>
    Task<Result<TestReportFile>> ExportTestCasesAsync(TestCaseQuery query, CancellationToken ct);
}

/// <summary>URLs of the evidence and export endpoints (implemented in the web composition root).</summary>
public static class TestResultRoutes
{
    public static string Artifact(Guid id) => $"/test-results/artifacts/{id}";

    public static string ArtifactDownload(Guid id) => $"/test-results/artifacts/{id}?download=true";

    public static string Export(long runId, string format) => $"/test-results/runs/{runId}/export/{format}";

    /// <summary>Filtered execution export (same query string as the results list).</summary>
    public const string ExecutionsExport = "/test-results/executions/export.csv";

    /// <summary>Filtered test case export (same query string as the test case list).</summary>
    public const string TestCasesExport = "/test-results/cases/export.csv";
}
