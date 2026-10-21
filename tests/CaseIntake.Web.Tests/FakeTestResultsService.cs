using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;

namespace CaseIntake.Web.Tests;

/// <summary>A scripted <see cref="ITestResultsService"/> for the test results pages (the real service has integration tests).</summary>
public sealed class FakeTestResultsService : ITestResultsService
{
    public TestDashboardDto Dashboard { get; set; } = new(null, null, [], [], [], [], 0);
    public Error? Failure { get; set; }
    public int ImportCalls { get; private set; }
    public ImportSummaryDto ImportResult { get; set; } = new(1, 0, 3, 1, []);
    public List<TestRunSummaryDto> Runs { get; } = [];
    public List<TestExecutionRowDto> Executions { get; } = [];
    public Dictionary<long, TestExecutionDetailDto> Details { get; } = [];

    public static TestRunSummaryDto Run(long id, int passed, int failed, int skipped = 0, int notExecuted = 0, TestRunStatus status = TestRunStatus.Completed) => new(
        id, $"run-{id}", status, new DateTimeOffset(2026, 10, (int)Math.Min(28, id), 9, 0, 0, TimeSpan.Zero), null, TimeSpan.FromSeconds(90),
        "docker compose tests", "main", "abcdef1234567890", "build-1", "image-1", "scripts/run-tests.sh",
        new TestOutcomeCounts(passed + failed + skipped + notExecuted, passed, failed, skipped, 0, notExecuted), DateTimeOffset.UnixEpoch, null);

    private Result<T> Respond<T>(T value) => Failure is { } f ? Result<T>.Failure(f) : Result<T>.Success(value);

    public Task<Result<TestDashboardDto>> GetDashboardAsync(CancellationToken ct) => Task.FromResult(Respond(Dashboard));

    public Task<Result<PagedList<TestRunSummaryDto>>> ListRunsAsync(TestRunQuery query, CancellationToken ct) =>
        Task.FromResult(Respond(new PagedList<TestRunSummaryDto>(Runs, query.Page, query.PageSize, Runs.Count)));

    public Task<Result<TestRunDetailDto>> GetRunAsync(long runId, CancellationToken ct) =>
        Task.FromResult(Runs.FirstOrDefault(r => r.Id == runId) is { } run
            ? Respond(new TestRunDetailDto(run, "Linux", ".NET 10", "http://web-e2e:8080", [new TestSuiteDto("CaseIntake.E2E", TestType.EndToEnd, "Completed", 3, null)], []))
            : Result<TestRunDetailDto>.Failure(new Error(ErrorKind.NotFound, "TestRunNotFound", "The test run was not found.")));

    public Task<Result<TestRunComparisonDto>> CompareWithPreviousAsync(long runId, CancellationToken ct) =>
        Task.FromResult(Respond(new TestRunComparisonDto(Runs.First(r => r.Id == runId), null, [], [], [], [], [])));

    public Task<Result<PagedList<TestExecutionRowDto>>> ListExecutionsAsync(TestExecutionQuery query, CancellationToken ct)
    {
        var rows = Matching(query);
        LastExecutionQuery = query;
        return Task.FromResult(Respond(new PagedList<TestExecutionRowDto>(rows, query.Page, query.PageSize, rows.Count)));
    }

    public Task<Result<TestExecutionDetailDto>> GetExecutionAsync(long executionId, CancellationToken ct) =>
        Task.FromResult(Details.TryGetValue(executionId, out var d)
            ? Respond(d)
            : Result<TestExecutionDetailDto>.Failure(new Error(ErrorKind.NotFound, "TestExecutionNotFound", "The test result was not found.")));

    public Task<Result<PagedList<TestCaseDto>>> ListTestCasesAsync(TestCaseQuery query, CancellationToken ct)
    {
        LastCaseQuery = query;
        var items = MatchingCases(query);
        return Task.FromResult(Respond(new PagedList<TestCaseDto>(items, query.Page, query.PageSize, items.Count)));
    }

    public Task<Result<TestCaseHistoryDto>> GetTestCaseHistoryAsync(long testCaseId, int maxRuns, CancellationToken ct) =>
        Task.FromResult(Result<TestCaseHistoryDto>.Failure(new Error(ErrorKind.NotFound, "TestCaseNotFound", "Not found.")));

    public Task<Result<IReadOnlyList<FlakyTestDto>>> GetFlakyTestsAsync(int lastRuns, CancellationToken ct) =>
        Task.FromResult(Respond<IReadOnlyList<FlakyTestDto>>([]));

    public Task<Result<PagedList<TestArtifactListItemDto>>> ListArtifactsAsync(TestArtifactQuery query, CancellationToken ct) =>
        Task.FromResult(Respond(new PagedList<TestArtifactListItemDto>([], 1, 48, 0)));

    public Task<Result<IReadOnlyList<string>>> ListModulesAsync(CancellationToken ct) => Task.FromResult(Respond<IReadOnlyList<string>>(["E2E", "Web.Tests"]));

    public Task<Result<IReadOnlyList<string>>> ListEnvironmentsAsync(CancellationToken ct) => Task.FromResult(Respond<IReadOnlyList<string>>(["docker compose tests"]));

    public Task<Result<TestArtifactContent>> OpenArtifactAsync(Guid artifactId, bool download, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<TestReportFile>> ExportRunAsync(long runId, string format, CancellationToken ct) => throw new NotSupportedException();


    public List<TestCaseDto> Cases { get; } = [];
    public TestExecutionQuery? LastExecutionQuery { get; private set; }
    public TestCaseQuery? LastCaseQuery { get; private set; }

    /// <summary>The same Section 508 rule as the real service: only explicitly classified tests match "Only".</summary>
    private static bool Matches(AccessibilityClassification? a, Section508Filter filter) => filter switch
    {
        Section508Filter.Section508Only => a == AccessibilityClassification.Section508,
        Section508Filter.ExcludeSection508 => a != AccessibilityClassification.Section508,
        _ => true,
    };

    private List<TestExecutionRowDto> Matching(TestExecutionQuery query) =>
        [.. Executions.Where(e => (query.Outcomes is null || query.Outcomes.Contains(e.Outcome)) && Matches(e.Accessibility, query.Section508))];

    private List<TestCaseDto> MatchingCases(TestCaseQuery query) =>
        [.. Cases.Where(c => (query.LastOutcome is null || (c.LastOutcome ?? TestOutcome.NotExecuted) == query.LastOutcome) && Matches(c.Accessibility, query.Section508))];

    private static TestOutcomeCounts Count(IReadOnlyCollection<TestOutcome> outcomes) => new(outcomes.Count,
        outcomes.Count(o => o == TestOutcome.Passed), outcomes.Count(o => o == TestOutcome.Failed), outcomes.Count(o => o == TestOutcome.Skipped),
        outcomes.Count(o => o == TestOutcome.Blocked), outcomes.Count(o => o == TestOutcome.NotExecuted), outcomes.Count(o => o == TestOutcome.NotApplicable));

    public Task<Result<TestOutcomeCounts>> SummarizeExecutionsAsync(TestExecutionQuery query, CancellationToken ct) =>
        Task.FromResult(Respond(Count([.. Matching(query).Select(e => e.Outcome)])));

    public Task<Result<TestOutcomeCounts>> SummarizeTestCasesAsync(TestCaseQuery query, CancellationToken ct) =>
        Task.FromResult(Respond(Count([.. MatchingCases(query).Select(c => c.LastOutcome ?? TestOutcome.NotExecuted)])));

    public Task<Result<TestReportFile>> ExportExecutionsAsync(TestExecutionQuery query, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<TestReportFile>> ExportTestCasesAsync(TestCaseQuery query, CancellationToken ct) => throw new NotSupportedException();
    public Task<Result<ImportSummaryDto>> ImportAsync(CancellationToken ct)
    {
        ImportCalls++;
        return Task.FromResult(Respond(ImportResult));
    }
}
