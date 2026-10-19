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
        var rows = Executions.Where(e => query.Outcomes is null || query.Outcomes.Contains(e.Outcome)).ToList();
        return Task.FromResult(Respond(new PagedList<TestExecutionRowDto>(rows, query.Page, query.PageSize, rows.Count)));
    }

    public Task<Result<TestExecutionDetailDto>> GetExecutionAsync(long executionId, CancellationToken ct) =>
        Task.FromResult(Details.TryGetValue(executionId, out var d)
            ? Respond(d)
            : Result<TestExecutionDetailDto>.Failure(new Error(ErrorKind.NotFound, "TestExecutionNotFound", "The test result was not found.")));

    public Task<Result<PagedList<TestCaseDto>>> ListTestCasesAsync(TestCaseQuery query, CancellationToken ct) =>
        Task.FromResult(Respond(new PagedList<TestCaseDto>([], 1, 50, 0)));

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

    public Task<Result<ImportSummaryDto>> ImportAsync(CancellationToken ct)
    {
        ImportCalls++;
        return Task.FromResult(Respond(ImportResult));
    }
}
