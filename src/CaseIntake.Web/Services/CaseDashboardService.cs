using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using Microsoft.Extensions.Options;

namespace CaseIntake.Web.Services;

/// <summary>Dashboard settings (<c>Dashboard</c> configuration section). Defaults need no configuration.</summary>
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>The "Recent cases" card lists New cases created within this many days.</summary>
    public int NewCasesDays { get; set; } = 7;

    /// <summary>Upper bound of case summaries read for one dashboard; above it the figures say they are partial.</summary>
    public int MaxCasesScanned { get; set; } = 5000;

    /// <summary>Months shown in the trend charts, including the current month.</summary>
    public int TrendMonths { get; set; } = 12;

    /// <summary>Window of the "Recent activity" card (cases updated recently).</summary>
    public int RecentActivityDays { get; set; } = 14;
}

public sealed record MonthCount(DateOnly Month, int Count)
{
    public string Label => Month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Inclusive UTC bounds of the month, for drill-through links to the case list.</summary>
    public DateTimeOffset StartUtc => new(Month.Year, Month.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset EndUtc => StartUtc.AddMonths(1).AddTicks(-1);
}

/// <summary>Everything the dashboard shows, computed from one snapshot so the numbers agree with each other.</summary>
public sealed record CaseDashboard(
    int Total,
    int Open,
    int PendingReview,
    int Overdue,
    int Completed,
    int AssignedToMe,
    int AwaitingMyAction,
    IReadOnlyDictionary<CaseStatus, int> ByStatus,
    IReadOnlyDictionary<CasePriority, int> OpenByPriority,
    IReadOnlyDictionary<CaseCategory, int> OpenByCategory,
    IReadOnlyList<MonthCount> CreatedByMonth,
    IReadOnlyList<MonthCount> CompletedByMonth,
    IReadOnlyList<CaseSummaryDto> NewCases,
    IReadOnlyList<CaseSummaryDto> MyTasks,
    IReadOnlyList<CaseSummaryDto> RequiringAttention,
    IReadOnlyList<CaseSummaryDto> RecentActivity,
    IReadOnlySet<CaseStatus> ActionableStatuses,
    string? CurrentUserName,
    int NewCasesDays,
    DateOnly TodayUtc,
    bool Partial,
    int Scanned,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Builds the case dashboard from <see cref="ICaseService.ListAsync"/> only, so the service's authorization applies to
/// every figure (a user without <c>cases.view</c> gets Forbidden, not numbers), and nothing is hard-coded. Definitions
/// (ADR 0011, issue #21):
/// <list type="bullet">
/// <item>Open: New, Triaged or In Progress. Pending review: Resolved (waiting to be closed). Completed: Resolved or Closed.</item>
/// <item>Overdue: open and its due date is before today (UTC), the same rule as the case list's Overdue filter.</item>
/// <item>Assigned to me: open cases whose owner is the signed-in user.</item>
/// <item>My pending tasks: cases in a status from which the signed-in user holds a transition permission (the same
/// rule that produces the status buttons).</item>
/// <item>Recent cases: New cases created in the last <see cref="DashboardOptions.NewCasesDays"/> days, newest first.</item>
/// </list>
/// </summary>
public sealed class CaseDashboardService(
    ICaseService cases,
    ICurrentUser currentUser,
    ICaseWorkflow workflow,
    TimeProvider time,
    IOptions<DashboardOptions> options)
{
    public static readonly IReadOnlySet<CaseStatus> OpenStatuses = new HashSet<CaseStatus> { CaseStatus.New, CaseStatus.Triaged, CaseStatus.InProgress };

    /// <summary>Query string selecting the open statuses on the case list.</summary>
    public const string OpenStatusQuery = "status=New&status=Triaged&status=InProgress";

    private DashboardOptions Settings => options.Value;

    /// <summary>The name cases record for this user (owner, created by): the display (user) name, else the user ID.</summary>
    public static string? UserNameOf(OperationActor actor) =>
        !actor.IsAuthenticated ? null : string.IsNullOrWhiteSpace(actor.DisplayName) ? actor.UserId : actor.DisplayName;

    public async Task<Result<CaseDashboard>> LoadAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        var actionable = Actionable(actor);
        var snapshot = await ReadAllAsync(ct);
        if (snapshot.Error is { } error)
        {
            return Result<CaseDashboard>.Failure(error);
        }

        var all = snapshot.Items;
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var me = UserNameOf(actor);
        var newSince = now.AddDays(-Settings.NewCasesDays);
        var byStatus = Enum.GetValues<CaseStatus>().ToDictionary(s => s, s => all.Count(c => c.Status == s));
        var open = all.Where(c => OpenStatuses.Contains(c.Status)).ToList();
        var overdue = open.Where(c => CaseTriageRules.IsOverdue(c.Status, c.DueOn, today))
            .OrderBy(c => c.DueOn).ThenByDescending(c => c.Priority).ThenBy(c => c.Id).ToList();
        var mine = all.Where(c => actionable.Contains(c.Status)).OrderBy(c => c.UpdatedAtUtc).ThenBy(c => c.Id).ToList();
        var months = Months(now, Settings.TrendMonths);

        return Result<CaseDashboard>.Success(new CaseDashboard(
            Total: snapshot.TotalCount,
            Open: open.Count,
            PendingReview: byStatus[CaseStatus.Resolved],
            Overdue: overdue.Count,
            Completed: byStatus[CaseStatus.Resolved] + byStatus[CaseStatus.Closed],
            AssignedToMe: me is null ? 0 : open.Count(c => string.Equals(c.AssignedTo, me, StringComparison.Ordinal)),
            AwaitingMyAction: mine.Count,
            ByStatus: byStatus,
            OpenByPriority: Enum.GetValues<CasePriority>().OrderDescending().ToDictionary(p => p, p => open.Count(c => c.Priority == p)),
            OpenByCategory: Enum.GetValues<CaseCategory>().ToDictionary(k => k, k => open.Count(c => c.Category == k)),
            CreatedByMonth: CountByMonth(months, all.Select(c => c.CreatedAtUtc)),
            // A case's completion time is approximated by its last update (Resolved cases can still be edited).
            CompletedByMonth: CountByMonth(months, all.Where(c => c.Status is CaseStatus.Resolved or CaseStatus.Closed).Select(c => c.UpdatedAtUtc)),
            NewCases: [.. all.Where(c => c.Status == CaseStatus.New && c.CreatedAtUtc >= newSince)
                .OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id).Take(5)],
            MyTasks: [.. mine.Take(5)],
            RequiringAttention: [.. overdue.Take(5)],
            RecentActivity: [.. all.OrderByDescending(c => c.UpdatedAtUtc).ThenByDescending(c => c.Id).Take(5)],
            ActionableStatuses: actionable,
            CurrentUserName: me,
            NewCasesDays: Settings.NewCasesDays,
            TodayUtc: today,
            Partial: snapshot.Items.Count < snapshot.TotalCount,
            Scanned: snapshot.Items.Count,
            GeneratedAtUtc: now));
    }

    /// <summary>
    /// Statuses from which the signed-in user may move a case: an edge of the domain workflow for which the user holds
    /// the transition permission. Mirrors how <c>CaseDetailDto.AllowedNextStatuses</c> is produced; no new rule.
    /// </summary>
    public async Task<IReadOnlySet<CaseStatus>> ActionableStatusesAsync(CancellationToken ct) =>
        Actionable(await currentUser.GetAsync(ct));

    private IReadOnlySet<CaseStatus> Actionable(OperationActor actor) =>
        Enum.GetValues<CaseStatus>()
            .Where(from => workflow.NextStatuses(from).Any(to => actor.HasPermission(Permissions.Transition(from, to))))
            .ToHashSet();

    private sealed record Snapshot(List<CaseSummaryDto> Items, int TotalCount, Error? Error);

    private async Task<Snapshot> ReadAllAsync(CancellationToken ct)
    {
        const int pageSize = 100;   // CaseService.MaxPageSize
        List<CaseSummaryDto> items = [];
        var page = 1;
        var total = 0;
        while (items.Count < Settings.MaxCasesScanned)
        {
            var result = await cases.ListAsync(new CaseListQuery(page, pageSize, SortBy: CaseSortField.Id, Direction: SortDirection.Ascending), ct);
            if (!result.IsSuccess)
            {
                return new Snapshot([], 0, result.Error);
            }
            total = result.Value!.TotalCount;
            items.AddRange(result.Value.Items);
            if (result.Value.Items.Count < pageSize || items.Count >= total)
            {
                break;
            }
            page++;
        }
        return new Snapshot(items.Count > Settings.MaxCasesScanned ? items[..Settings.MaxCasesScanned] : items, total, null);
    }

    private static List<DateOnly> Months(DateTimeOffset now, int count)
    {
        var current = new DateOnly(now.UtcDateTime.Year, now.UtcDateTime.Month, 1);
        return [.. Enumerable.Range(0, Math.Max(count, 1)).Select(i => current.AddMonths(i - count + 1))];
    }

    private static List<MonthCount> CountByMonth(List<DateOnly> months, IEnumerable<DateTimeOffset> times)
    {
        var counts = times
            .Select(t => new DateOnly(t.UtcDateTime.Year, t.UtcDateTime.Month, 1))
            .GroupBy(m => m)
            .ToDictionary(g => g.Key, g => g.Count());
        return [.. months.Select(m => new MonthCount(m, counts.GetValueOrDefault(m)))];
    }
}
