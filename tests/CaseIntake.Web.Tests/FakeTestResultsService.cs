using System.Runtime.CompilerServices;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;

namespace CaseIntake.Web.Tests;

/// <summary>A scripted <see cref="ITestResultsService"/> for the test results pages (the real service has integration tests).</summary>
public sealed class FakeTestResultsService : ITestResultsService
{
    public TestDashboardDto Dashboard { get; set; } = new(null, null, [], [], [], [], 0);

    /// <summary>When set, calls fail with this error (all calls, or only those named in <see cref="Only"/>).</summary>
    public Error? Failure { get; set; }

    /// <summary>When set, calls throw this exception (all calls, or only those named in <see cref="Only"/>).</summary>
    public Exception? Throw { get; set; }

    /// <summary>Limits <see cref="Failure"/>, <see cref="Throw"/> and <see cref="Gate"/> to these method names (for example nameof(ListExecutionsAsync)).</summary>
    public HashSet<string>? Only { get; set; }

    /// <summary>When set, calls wait for this task first, so a page can be observed while it is loading.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public int ImportCalls { get; private set; }
    public ImportSummaryDto ImportResult { get; set; } = new(1, 0, 3, 1, []);
    public List<TestRunSummaryDto> Runs { get; } = [];
    public List<TestExecutionRowDto> Executions { get; } = [];
    public Dictionary<long, TestExecutionDetailDto> Details { get; } = [];
    public List<TestCaseDto> TestCases { get; } = [];
    public Dictionary<long, TestCaseHistoryDto> Histories { get; } = [];
    public List<FlakyTestDto> Flaky { get; } = [];
    public List<TestArtifactListItemDto> Artifacts { get; } = [];
    public List<TestArtifactDto> RunArtifacts { get; } = [];

    /// <summary>The comparison returned for a run; when absent the run is compared with nothing.</summary>
    public Dictionary<long, TestRunComparisonDto> Comparisons { get; } = [];

    public List<TestRunQuery> RunQueries { get; } = [];
    public List<TestExecutionQuery> ExecutionQueries { get; } = [];
    public List<TestCaseQuery> CaseQueries { get; } = [];
    public List<TestArtifactQuery> ArtifactQueries { get; } = [];

    public static TestRunSummaryDto Run(long id, int passed, int failed, int skipped = 0, int notExecuted = 0, TestRunStatus status = TestRunStatus.Completed, int blocked = 0, string? notes = null) => new(
        id, $"run-{id}", status, new DateTimeOffset(2026, 10, (int)Math.Min(28, id), 9, 0, 0, TimeSpan.Zero), null, TimeSpan.FromSeconds(90),
        "docker compose tests", "main", "abcdef1234567890", "build-1", "image-1", "scripts/run-tests.sh",
        new TestOutcomeCounts(passed + failed + skipped + blocked + notExecuted, passed, failed, skipped, blocked, notExecuted), DateTimeOffset.UnixEpoch, notes);

    public static TestCaseDto TestCase(long id, string title, TestOutcome? last = TestOutcome.Passed, string? story = "E5-S6", string? requirements = "R-4.2-8") => new(
        id, $"Ns.Class.{title.Replace(' ', '_')}", title, "E2E", TestType.EndToEnd, "Checks the page", story, requirements, null, "High", null, null,
        last, 1, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    public static TestArtifactDto Artifact(TestArtifactKind kind, string fileName, string? caption = null, bool purged = false) => new(
        Guid.NewGuid(), kind, fileName, kind == TestArtifactKind.Screenshot ? "image/png" : "text/plain", 2048, DateTimeOffset.UnixEpoch, purged, caption);

    private async Task<Result<T>> Respond<T>(T value, [CallerMemberName] string call = "")
    {
        var applies = Only is null || Only.Contains(call);
        if (applies && Gate is { } gate)
        {
            await gate.Task;
        }
        if (applies && Throw is { } ex)
        {
            throw ex;
        }
        return applies && Failure is { } f ? Result<T>.Failure(f) : Result<T>.Success(value);
    }

    private static PagedList<T> Page<T>(IReadOnlyList<T> all, int page, int pageSize) =>
        new([.. all.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, all.Count);

    public Task<Result<TestDashboardDto>> GetDashboardAsync(CancellationToken ct) => Respond(Dashboard);

    public Task<Result<PagedList<TestRunSummaryDto>>> ListRunsAsync(TestRunQuery query, CancellationToken ct)
    {
        RunQueries.Add(query);
        return Respond(Page(Runs, query.Page, query.PageSize));
    }

    public async Task<Result<TestRunDetailDto>> GetRunAsync(long runId, CancellationToken ct) =>
        Runs.FirstOrDefault(r => r.Id == runId) is { } run
            ? await Respond(new TestRunDetailDto(run, "Linux", ".NET 10", "http://web-e2e:8080", [new TestSuiteDto("CaseIntake.E2E", TestType.EndToEnd, "Completed", 3, null), new TestSuiteDto("CaseIntake.Web.Tests", TestType.Component, "NoResults", 0, "No trx file")], RunArtifacts))
            : Result<TestRunDetailDto>.Failure(new Error(ErrorKind.NotFound, "TestRunNotFound", "The test run was not found."));

    public Task<Result<TestRunComparisonDto>> CompareWithPreviousAsync(long runId, CancellationToken ct) =>
        Respond(Comparisons.TryGetValue(runId, out var c) ? c : new TestRunComparisonDto(Runs.First(r => r.Id == runId), null, [], [], [], [], []));

    public Task<Result<PagedList<TestExecutionRowDto>>> ListExecutionsAsync(TestExecutionQuery query, CancellationToken ct)
    {
        ExecutionQueries.Add(query);
        var rows = Executions.Where(e => query.Outcomes is null || query.Outcomes.Contains(e.Outcome)).ToList();
        return Respond(Page(rows, query.Page, query.PageSize));
    }

    public async Task<Result<TestExecutionDetailDto>> GetExecutionAsync(long executionId, CancellationToken ct) =>
        Details.TryGetValue(executionId, out var d)
            ? await Respond(d)
            : Result<TestExecutionDetailDto>.Failure(new Error(ErrorKind.NotFound, "TestExecutionNotFound", "The test result was not found."));

    public Task<Result<PagedList<TestCaseDto>>> ListTestCasesAsync(TestCaseQuery query, CancellationToken ct)
    {
        CaseQueries.Add(query);
        return Respond(Page(TestCases, query.Page, query.PageSize));
    }

    public async Task<Result<TestCaseHistoryDto>> GetTestCaseHistoryAsync(long testCaseId, int maxRuns, CancellationToken ct) =>
        Histories.TryGetValue(testCaseId, out var h)
            ? await Respond(h)
            : Result<TestCaseHistoryDto>.Failure(new Error(ErrorKind.NotFound, "TestCaseNotFound", "The test case was not found."));

    public Task<Result<IReadOnlyList<FlakyTestDto>>> GetFlakyTestsAsync(int lastRuns, CancellationToken ct) =>
        Respond<IReadOnlyList<FlakyTestDto>>(Flaky);

    public Task<Result<PagedList<TestArtifactListItemDto>>> ListArtifactsAsync(TestArtifactQuery query, CancellationToken ct)
    {
        ArtifactQueries.Add(query);
        return Respond(Page(Artifacts, query.Page, query.PageSize));
    }

    public Task<Result<IReadOnlyList<string>>> ListModulesAsync(CancellationToken ct) => Respond<IReadOnlyList<string>>(["E2E", "Web.Tests"]);

    public Task<Result<IReadOnlyList<string>>> ListEnvironmentsAsync(CancellationToken ct) => Respond<IReadOnlyList<string>>(["docker compose tests"]);

    public Task<Result<TestArtifactContent>> OpenArtifactAsync(Guid artifactId, bool download, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<TestReportFile>> ExportRunAsync(long runId, string format, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<ImportSummaryDto>> ImportAsync(CancellationToken ct)
    {
        ImportCalls++;
        return Respond(ImportResult);
    }
}
