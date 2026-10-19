using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.Persistence;
using CaseIntake.TestResults.Import;
using CaseIntake.TestResults.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.TestResults;

/// <summary>
/// <see cref="ITestResultsService"/>: reads the actor once per call and requires <c>testresults.view</c> (import:
/// <c>testresults.manage</c>), uses a short-lived context per operation, and audits evidence access and exports.
/// </summary>
internal sealed class TestResultsService(
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    TestResultImporter importer,
    IOptions<TestResultsOptions> options,
    TimeProvider time,
    ILogger<TestResultsService> logger) : ITestResultsService
{
    private const int MaxPageSize = 200;
    private const string LikeEscape = "\\";

    public async Task<Result<TestDashboardDto>> GetDashboardAsync(CancellationToken ct)
    {
        if (await DeniedAsync<TestDashboardDto>(Permissions.TestResultsView, "dashboard", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var recent = await db.TestRuns.AsNoTracking().Include(r => r.Environment)
            .OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id).Take(15).ToListAsync(ct);
        var known = await db.TestCases.CountAsync(ct);
        if (recent.Count == 0)
        {
            return Result<TestDashboardDto>.Success(new TestDashboardDto(null, null, [], [], [], [], known));
        }

        var latest = recent[0];
        var latestExecutions = db.TestExecutions.AsNoTracking().Where(e => e.RunId == latest.Id);
        var failuresByModule = (await latestExecutions.Where(e => e.Outcome == TestOutcome.Failed)
                .Select(e => e.TestCase.Module).GroupBy(m => m).Select(g => new { Module = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Module, StringComparer.Ordinal)
            .Select(x => new LabelCount(x.Module, x.Count)).ToList();
        var coverage = await latestExecutions.Where(e => e.Browser != null)
            .GroupBy(e => new { e.Browser, e.Viewport }).Select(g => new { g.Key.Browser, g.Key.Viewport, Count = g.Count() })
            .ToListAsync(ct);
        var byType = await latestExecutions.Select(e => e.TestCase.Type).GroupBy(t => t).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return Result<TestDashboardDto>.Success(new TestDashboardDto(
            Summary(latest),
            await CompareAsync(db, latest, ct),
            [.. recent.Select(Summary)],
            failuresByModule,
            [.. coverage.OrderBy(c => c.Browser).ThenBy(c => c.Viewport).Select(c => new LabelCount($"{c.Browser} {c.Viewport}".Trim(), c.Count))],
            [.. byType.OrderBy(t => t.Key).Select(t => new LabelCount(TypeLabel(t.Key), t.Count))],
            known));
    }

    public async Task<Result<PagedList<TestRunSummaryDto>>> ListRunsAsync(TestRunQuery query, CancellationToken ct)
    {
        if (await DeniedAsync<PagedList<TestRunSummaryDto>>(Permissions.TestResultsView, "runs", ct) is { } denied)
        {
            return denied;
        }
        if (Paging(query.Page, query.PageSize) is { } invalid)
        {
            return Result<PagedList<TestRunSummaryDto>>.Failure(invalid);
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var runs = db.TestRuns.AsNoTracking().Include(r => r.Environment).AsQueryable();
        if (query.Status is { } status)
        {
            runs = runs.Where(r => r.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.Environment))
        {
            runs = runs.Where(r => r.Environment.Name == query.Environment);
        }
        if (Pattern(query.Search) is { } pattern)
        {
            runs = runs.Where(r => EF.Functions.ILike(r.RunKey, pattern, LikeEscape)
                || (r.GitBranch != null && EF.Functions.ILike(r.GitBranch, pattern, LikeEscape))
                || (r.GitCommit != null && EF.Functions.ILike(r.GitCommit, pattern, LikeEscape))
                || (r.BuildId != null && EF.Functions.ILike(r.BuildId, pattern, LikeEscape)));
        }
        if (query.FromDate is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            runs = runs.Where(r => r.StartedAtUtc >= start);
        }
        if (query.ToDate is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            runs = runs.Where(r => r.StartedAtUtc < end);
        }
        var total = await runs.CountAsync(ct);
        var items = await runs.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedList<TestRunSummaryDto>>.Success(new([.. items.Select(Summary)], query.Page, query.PageSize, total));
    }

    public async Task<Result<TestRunDetailDto>> GetRunAsync(long runId, CancellationToken ct)
    {
        if (await DeniedAsync<TestRunDetailDto>(Permissions.TestResultsView, $"run {runId}", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var run = await db.TestRuns.AsNoTracking().Include(r => r.Environment).FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return Result<TestRunDetailDto>.Failure(TestResultErrors.RunNotFound);
        }
        var artifacts = await db.TestArtifacts.AsNoTracking().Where(a => a.RunId == runId && a.ExecutionId == null)
            .OrderBy(a => a.Kind).ThenBy(a => a.FileName).ToListAsync(ct);
        return Result<TestRunDetailDto>.Success(new TestRunDetailDto(Summary(run), run.Environment.OperatingSystem,
            run.Environment.Runtime, run.Environment.BaseUrl, Suites(run.SuitesJson), [.. artifacts.Select(Artifact)]));
    }

    public async Task<Result<TestRunComparisonDto>> CompareWithPreviousAsync(long runId, CancellationToken ct)
    {
        if (await DeniedAsync<TestRunComparisonDto>(Permissions.TestResultsView, $"run {runId}", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var run = await db.TestRuns.AsNoTracking().Include(r => r.Environment).FirstOrDefaultAsync(r => r.Id == runId, ct);
        return run is null
            ? Result<TestRunComparisonDto>.Failure(TestResultErrors.RunNotFound)
            : Result<TestRunComparisonDto>.Success(await CompareAsync(db, run, ct));
    }

    public async Task<Result<PagedList<TestExecutionRowDto>>> ListExecutionsAsync(TestExecutionQuery query, CancellationToken ct)
    {
        if (await DeniedAsync<PagedList<TestExecutionRowDto>>(Permissions.TestResultsView, "executions", ct) is { } denied)
        {
            return denied;
        }
        if (Paging(query.Page, query.PageSize) is { } invalid)
        {
            return Result<PagedList<TestExecutionRowDto>>.Failure(invalid);
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var executions = db.TestExecutions.AsNoTracking().AsQueryable();
        if (query.RunId is { } runId)
        {
            executions = executions.Where(e => e.RunId == runId);
        }
        if (query.Outcomes is { Count: > 0 } outcomes)
        {
            var wanted = outcomes.Distinct().ToArray();
            executions = executions.Where(e => wanted.Contains(e.Outcome));
        }
        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            executions = executions.Where(e => e.TestCase.Module == query.Module);
        }
        if (query.Type is { } type)
        {
            executions = executions.Where(e => e.TestCase.Type == type);
        }
        if (Pattern(query.Search) is { } pattern)
        {
            executions = executions.Where(e => EF.Functions.ILike(e.TestCase.Title, pattern, LikeEscape) || EF.Functions.ILike(e.TestCase.FullName, pattern, LikeEscape));
        }
        var total = await executions.CountAsync(ct);
        var rows = await executions
            .OrderBy(e => e.Outcome == TestOutcome.Failed ? 0 : e.Outcome == TestOutcome.NotExecuted ? 1 : e.Outcome == TestOutcome.Blocked ? 2 : 3)
            .ThenBy(e => e.TestCase.Module).ThenBy(e => e.TestCase.Title).ThenBy(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(e => new TestExecutionRowDto(e.Id, e.RunId, e.TestCaseId, e.TestCase.Title, e.TestCase.FullName, e.TestCase.Module,
                e.TestCase.Type, e.Outcome, TimeSpan.FromMilliseconds(e.DurationMs), e.Browser, e.Viewport, e.ErrorMessage, e.Artifacts.Count))
            .ToListAsync(ct);
        return Result<PagedList<TestExecutionRowDto>>.Success(new([.. rows.Select(TrimMessage)], query.Page, query.PageSize, total));
    }

    public async Task<Result<TestExecutionDetailDto>> GetExecutionAsync(long executionId, CancellationToken ct)
    {
        if (await DeniedAsync<TestExecutionDetailDto>(Permissions.TestResultsView, $"execution {executionId}", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var e = await db.TestExecutions.AsNoTracking()
            .Include(x => x.TestCase).Include(x => x.Run).ThenInclude(r => r.Environment)
            .Include(x => x.Steps).Include(x => x.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == executionId, ct);
        if (e is null)
        {
            return Result<TestExecutionDetailDto>.Failure(TestResultErrors.ExecutionNotFound);
        }
        return Result<TestExecutionDetailDto>.Success(new TestExecutionDetailDto(e.Id, Case(e.TestCase), Summary(e.Run), e.Outcome,
            e.StartedAtUtc, e.FinishedAtUtc, TimeSpan.FromMilliseconds(e.DurationMs), e.Browser, e.Viewport, e.ActualResult,
            e.ErrorMessage, e.StackTrace, e.StandardOutput, e.ConsoleLog, e.NetworkLog,
            [.. e.Steps.OrderBy(s => s.Order).Select(s => new TestStepDto(s.Order, s.Name, s.Outcome, TimeSpan.FromMilliseconds(s.DurationMs), s.Expected, s.Actual, s.Message))],
            [.. e.Artifacts.OrderBy(a => a.Kind).ThenBy(a => a.CreatedAtUtc).ThenBy(a => a.FileName).Select(Artifact)],
            [.. (e.TestCase.Requirements ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]));
    }

    public async Task<Result<PagedList<TestCaseDto>>> ListTestCasesAsync(TestCaseQuery query, CancellationToken ct)
    {
        if (await DeniedAsync<PagedList<TestCaseDto>>(Permissions.TestResultsView, "test cases", ct) is { } denied)
        {
            return denied;
        }
        if (Paging(query.Page, query.PageSize) is { } invalid)
        {
            return Result<PagedList<TestCaseDto>>.Failure(invalid);
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var cases = db.TestCases.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            cases = cases.Where(c => c.Module == query.Module);
        }
        if (query.Type is { } type)
        {
            cases = cases.Where(c => c.Type == type);
        }
        if (query.LastOutcome is { } outcome)
        {
            cases = cases.Where(c => c.LastOutcome == outcome);
        }
        if (Pattern(query.Search) is { } pattern)
        {
            cases = cases.Where(c => EF.Functions.ILike(c.Title, pattern, LikeEscape) || EF.Functions.ILike(c.FullName, pattern, LikeEscape)
                || (c.Story != null && EF.Functions.ILike(c.Story, pattern, LikeEscape))
                || (c.Requirements != null && EF.Functions.ILike(c.Requirements, pattern, LikeEscape)));
        }
        var total = await cases.CountAsync(ct);
        var items = await cases.OrderBy(c => c.LastOutcome == TestOutcome.Failed ? 0 : 1).ThenBy(c => c.Module).ThenBy(c => c.Title).ThenBy(c => c.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedList<TestCaseDto>>.Success(new([.. items.Select(Case)], query.Page, query.PageSize, total));
    }

    public async Task<Result<TestCaseHistoryDto>> GetTestCaseHistoryAsync(long testCaseId, int maxRuns, CancellationToken ct)
    {
        if (await DeniedAsync<TestCaseHistoryDto>(Permissions.TestResultsView, $"test case {testCaseId}", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var testCase = await db.TestCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == testCaseId, ct);
        if (testCase is null)
        {
            return Result<TestCaseHistoryDto>.Failure(TestResultErrors.TestCaseNotFound);
        }
        var history = await db.TestExecutionHistory.AsNoTracking().Where(h => h.TestCaseId == testCaseId)
            .OrderByDescending(h => h.RunStartedAtUtc).ThenByDescending(h => h.RunId).Take(Math.Clamp(maxRuns, 1, 200))
            .Select(h => new TestHistoryPointDto(h.RunId, h.RunKey, h.RunStartedAtUtc, h.Outcome, TimeSpan.FromMilliseconds(h.DurationMs), h.GitCommit, h.ExecutionId))
            .ToListAsync(ct);
        return Result<TestCaseHistoryDto>.Success(new TestCaseHistoryDto(Case(testCase), history));
    }

    public async Task<Result<IReadOnlyList<FlakyTestDto>>> GetFlakyTestsAsync(int lastRuns, CancellationToken ct)
    {
        if (await DeniedAsync<IReadOnlyList<FlakyTestDto>>(Permissions.TestResultsView, "history", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var runIds = await db.TestRuns.AsNoTracking().OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id)
            .Take(Math.Clamp(lastRuns, 2, 100)).Select(r => r.Id).ToListAsync(ct);
        var rows = await db.TestExecutionHistory.AsNoTracking()
            .Where(h => runIds.Contains(h.RunId) && (h.Outcome == TestOutcome.Passed || h.Outcome == TestOutcome.Failed || h.Outcome == TestOutcome.Blocked))
            .ToListAsync(ct);
        var flaky = rows.GroupBy(h => h.TestCaseId)
            .Select(g => new
            {
                First = g.OrderByDescending(h => h.RunStartedAtUtc).First(),
                Runs = g.Count(),
                Passed = g.Count(h => h.Outcome == TestOutcome.Passed),
                Failed = g.Count(h => h.Outcome != TestOutcome.Passed),
            })
            // Flaky: both passed and failed recently. Recurring: failed in two or more of the recent runs.
            .Where(x => (x.Passed > 0 && x.Failed > 0) || x.Failed >= 2)
            .OrderByDescending(x => x.Failed).ThenBy(x => x.First.TestTitle)
            .Select(x => new FlakyTestDto(x.First.TestCaseId, x.First.TestTitle, x.First.Module, x.Runs, x.Passed, x.Failed, x.First.Outcome))
            .ToList();
        return Result<IReadOnlyList<FlakyTestDto>>.Success(flaky);
    }

    public async Task<Result<PagedList<TestArtifactListItemDto>>> ListArtifactsAsync(TestArtifactQuery query, CancellationToken ct)
    {
        if (await DeniedAsync<PagedList<TestArtifactListItemDto>>(Permissions.TestResultsView, "evidence", ct) is { } denied)
        {
            return denied;
        }
        if (Paging(query.Page, query.PageSize) is { } invalid)
        {
            return Result<PagedList<TestArtifactListItemDto>>.Failure(invalid);
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var artifacts = db.TestArtifacts.AsNoTracking().AsQueryable();
        if (query.RunId is { } runId)
        {
            artifacts = artifacts.Where(a => a.RunId == runId);
        }
        if (query.Kind is { } kind)
        {
            artifacts = artifacts.Where(a => a.Kind == kind);
        }
        var joined = from a in artifacts
                     join e in db.TestExecutions on a.ExecutionId equals e.Id into executions
                     from e in executions.DefaultIfEmpty()
                     select new { a, RunKey = a.Run.RunKey, TestTitle = e == null ? null : e.TestCase.Title, Outcome = e == null ? (TestOutcome?)null : e.Outcome };
        if (Pattern(query.Search) is { } pattern)
        {
            joined = joined.Where(x => EF.Functions.ILike(x.a.FileName, pattern, LikeEscape)
                || (x.a.Caption != null && EF.Functions.ILike(x.a.Caption, pattern, LikeEscape))
                || (x.TestTitle != null && EF.Functions.ILike(x.TestTitle, pattern, LikeEscape)));
        }
        var total = await joined.CountAsync(ct);
        var page = await joined.OrderByDescending(x => x.a.CreatedAtUtc).ThenBy(x => x.a.FileName)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedList<TestArtifactListItemDto>>.Success(new(
            [.. page.Select(x => new TestArtifactListItemDto(Artifact(x.a), x.a.RunId, x.RunKey, x.a.ExecutionId, x.TestTitle, x.Outcome))],
            query.Page, query.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<string>>> ListModulesAsync(CancellationToken ct)
    {
        if (await DeniedAsync<IReadOnlyList<string>>(Permissions.TestResultsView, "modules", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return Result<IReadOnlyList<string>>.Success(await db.TestCases.AsNoTracking().Select(c => c.Module).Distinct().OrderBy(m => m).ToListAsync(ct));
    }

    public async Task<Result<IReadOnlyList<string>>> ListEnvironmentsAsync(CancellationToken ct)
    {
        if (await DeniedAsync<IReadOnlyList<string>>(Permissions.TestResultsView, "environments", ct) is { } denied)
        {
            return denied;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return Result<IReadOnlyList<string>>.Success(await db.TestEnvironments.AsNoTracking().Select(e => e.Name).Distinct().OrderBy(n => n).ToListAsync(ct));
    }

    public async Task<Result<TestArtifactContent>> OpenArtifactAsync(Guid artifactId, bool download, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.TestResultsView))
        {
            TestResultsLog.AuthorizationDenied(logger, actor.UserId, Permissions.TestResultsView, $"artifact {artifactId}");
            return Result<TestArtifactContent>.Failure(TestResultErrors.Forbidden);
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var artifact = await db.TestArtifacts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == artifactId, ct);
        if (artifact is null)
        {
            return Result<TestArtifactContent>.Failure(TestResultErrors.ArtifactNotFound);
        }
        if (artifact.Purged)
        {
            return Result<TestArtifactContent>.Failure(TestResultErrors.ArtifactPurged);
        }
        var path = TestResultImporter.SafeChild(options.Value.RootPath, artifact.RelativePath);
        if (path is null || !File.Exists(path))
        {
            TestResultsLog.EvidenceMissing(logger, artifactId);
            return Result<TestArtifactContent>.Failure(TestResultErrors.ArtifactNotFound);
        }

        var access = download ? "EvidenceDownloaded" : "EvidenceViewed";
        db.TestResultAuditEntries.Add(new TestResultAuditEntry
        {
            OccurredAtUtc = time.GetUtcNow(), ActorId = actor.UserId, Actor = actor.DisplayName ?? actor.UserId!, Action = access,
            RunId = artifact.RunId, ArtifactId = artifact.Id, Details = artifact.Kind.ToString(),
        });
        await db.SaveChangesAsync(ct);
        TestResultsLog.EvidenceAccessed(logger, artifactId, artifact.Kind.ToString(), download ? "downloaded" : "viewed", actor.UserId);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Result<TestArtifactContent>.Success(new TestArtifactContent(stream, artifact.FileName, artifact.ContentType, stream.Length, artifact.Kind));
    }

    public async Task<Result<TestReportFile>> ExportRunAsync(long runId, string format, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.TestResultsView))
        {
            TestResultsLog.AuthorizationDenied(logger, actor.UserId, Permissions.TestResultsView, $"run {runId} export");
            return Result<TestReportFile>.Failure(TestResultErrors.Forbidden);
        }
        if (format is not ("csv" or "html"))
        {
            return Result<TestReportFile>.Failure(TestResultErrors.UnknownFormat);
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var run = await db.TestRuns.AsNoTracking().Include(r => r.Environment).FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return Result<TestReportFile>.Failure(TestResultErrors.RunNotFound);
        }
        var rows = await db.TestExecutions.AsNoTracking().Where(e => e.RunId == runId)
            .OrderBy(e => e.TestCase.Module).ThenBy(e => e.TestCase.Title)
            .Select(e => new TestExecutionRowDto(e.Id, e.RunId, e.TestCaseId, e.TestCase.Title, e.TestCase.FullName, e.TestCase.Module,
                e.TestCase.Type, e.Outcome, TimeSpan.FromMilliseconds(e.DurationMs), e.Browser, e.Viewport, e.ErrorMessage, e.Artifacts.Count))
            .ToListAsync(ct);
        var summary = Summary(run);
        var comparison = await CompareAsync(db, run, ct);
        var file = format == "csv"
            ? TestReportWriter.Csv(summary, rows)
            : TestReportWriter.Html(summary, run.Environment, Suites(run.SuitesJson), comparison, rows, time.GetUtcNow());

        db.TestResultAuditEntries.Add(new TestResultAuditEntry
        {
            OccurredAtUtc = time.GetUtcNow(), ActorId = actor.UserId, Actor = actor.DisplayName ?? actor.UserId!, Action = "ReportExported",
            RunId = runId, Details = format,
        });
        await db.SaveChangesAsync(ct);
        TestResultsLog.ReportExported(logger, runId, format, actor.UserId);
        return Result<TestReportFile>.Success(file);
    }

    public async Task<Result<ImportSummaryDto>> ImportAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.TestResultsManage))
        {
            TestResultsLog.AuthorizationDenied(logger, actor.UserId, Permissions.TestResultsManage, "import");
            return Result<ImportSummaryDto>.Failure(TestResultErrors.Forbidden);
        }
        return Result<ImportSummaryDto>.Success(await importer.ImportPendingAsync(actor.DisplayName ?? actor.UserId!, actor.UserId, ct));
    }

    // ---- helpers ----

    private async Task<Result<T>?> DeniedAsync<T>(string permission, string resource, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (actor.HasPermission(permission))
        {
            return null;
        }
        TestResultsLog.AuthorizationDenied(logger, actor.UserId, permission, resource);
        return Result<T>.Failure(TestResultErrors.Forbidden);
    }

    private static Error? Paging(int page, int pageSize) =>
        page < 1 ? TestResultErrors.InvalidQuery("Page", "Page must be 1 or greater.")
        : pageSize is < 1 or > MaxPageSize ? TestResultErrors.InvalidQuery("PageSize", $"Page size must be between 1 and {MaxPageSize}.")
        : null;

    private static string? Pattern(string? search)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (text.Length > 200)
        {
            text = text[..200];
        }
        return "%" + text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal) + "%";
    }

    private static async Task<TestRunComparisonDto> CompareAsync(AppDbContext db, TestRun run, CancellationToken ct)
    {
        var previous = await db.TestRuns.AsNoTracking().Include(r => r.Environment)
            .Where(r => r.StartedAtUtc < run.StartedAtUtc || (r.StartedAtUtc == run.StartedAtUtc && r.Id < run.Id))
            .OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        var current = await Rows(db, run.Id, ct);
        if (previous is null)
        {
            return new TestRunComparisonDto(Summary(run), null, [], [], [.. current.Where(r => r.Outcome == TestOutcome.Failed)], [], []);
        }
        var before = (await Rows(db, previous.Id, ct)).ToDictionary(r => r.TestCaseId);
        var now = current.ToDictionary(r => r.TestCaseId);
        return new TestRunComparisonDto(Summary(run), Summary(previous),
            NewlyFailed: [.. current.Where(r => r.Outcome == TestOutcome.Failed && before.TryGetValue(r.TestCaseId, out var b) && b.Outcome == TestOutcome.Passed)],
            NewlyFixed: [.. current.Where(r => r.Outcome == TestOutcome.Passed && before.TryGetValue(r.TestCaseId, out var b) && b.Outcome is TestOutcome.Failed or TestOutcome.Blocked)],
            StillFailing: [.. current.Where(r => r.Outcome == TestOutcome.Failed && before.TryGetValue(r.TestCaseId, out var b) && b.Outcome == TestOutcome.Failed)],
            NewTests: [.. current.Where(r => !before.ContainsKey(r.TestCaseId)).Select(r => r.TestTitle).Order(StringComparer.Ordinal)],
            RemovedTests: [.. before.Values.Where(r => !now.ContainsKey(r.TestCaseId)).Select(r => r.TestTitle).Order(StringComparer.Ordinal)]);
    }

    private static async Task<List<TestExecutionRowDto>> Rows(AppDbContext db, long runId, CancellationToken ct) =>
        [.. (await db.TestExecutions.AsNoTracking().Where(e => e.RunId == runId)
            .Select(e => new TestExecutionRowDto(e.Id, e.RunId, e.TestCaseId, e.TestCase.Title, e.TestCase.FullName, e.TestCase.Module,
                e.TestCase.Type, e.Outcome, TimeSpan.FromMilliseconds(e.DurationMs), e.Browser, e.Viewport, e.ErrorMessage, e.Artifacts.Count))
            .ToListAsync(ct)).Select(TrimMessage)];

    private static TestExecutionRowDto TrimMessage(TestExecutionRowDto row) =>
        row.FailureMessage is { Length: > 300 } m ? row with { FailureMessage = m[..300] + "…" } : row;

    internal static TestRunSummaryDto Summary(TestRun r) => new(r.Id, r.RunKey, r.Status, r.StartedAtUtc, r.FinishedAtUtc,
        TimeSpan.FromMilliseconds(r.DurationMs), r.Environment?.Name ?? "unknown", r.GitBranch, r.GitCommit, r.BuildId, r.ImageVersion,
        r.Runner, new TestOutcomeCounts(r.Total, r.Passed, r.Failed, r.Skipped, r.Blocked, r.NotExecuted), r.ImportedAtUtc, r.Notes);

    private static TestCaseDto Case(TestCase c) => new(c.Id, c.FullName, c.Title, c.Module, c.Type, c.Description, c.Story,
        c.Requirements, c.AcceptanceCriteria, c.Priority, c.Preconditions, c.ExpectedResult, c.LastOutcome, c.LastRunId, c.FirstSeenAtUtc);

    private static TestArtifactDto Artifact(TestArtifact a) => new(a.Id, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAtUtc, a.Purged, a.Caption);

    internal static string TypeLabel(TestType type) => type switch
    {
        TestType.Unit => "Unit",
        TestType.Component => "Component (bUnit)",
        TestType.Integration => "Integration",
        TestType.EndToEnd => "End-to-end (Playwright)",
        _ => type.ToString(),
    };

    private static List<TestSuiteDto> Suites(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return [.. doc.RootElement.EnumerateArray().Select(s => new TestSuiteDto(
                s.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                TestResultImporter.ParseType(s.TryGetProperty("type", out var t) ? t.GetString() : null),
                s.TryGetProperty("status", out var st) ? st.GetString() ?? string.Empty : string.Empty,
                s.TryGetProperty("tests", out var c) && c.TryGetInt32(out var count) ? count : 0,
                s.TryGetProperty("message", out var m) ? m.GetString() : null))];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
