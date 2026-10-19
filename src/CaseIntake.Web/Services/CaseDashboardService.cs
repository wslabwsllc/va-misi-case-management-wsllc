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

    /// <summary>An open case (New, Triaged, In Progress) not updated for this many days counts as overdue.</summary>
    public int OverdueAfterDays { get; set; } = 14;

    /// <summary>Upper bound of case summaries read for one dashboard; above it the figures say they are partial.</summary>
    public int MaxCasesScanned { get; set; } = 5000;

    /// <summary>Months shown in the trend charts, including the current month.</summary>
    public int TrendMonths { get; set; } = 12;

    /// <summary>Window of the Notifications page (recent activity).</summary>
    public int RecentActivityDays { get; set; } = 14;
}

public sealed record MonthCount(DateOnly Month, int Count)
{
    public string Label => Month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Everything the dashboard shows, computed from one snapshot so the numbers agree with each other.</summary>
public sealed record CaseDashboard(
    int Total,
    int Open,
    int PendingReview,
    int Overdue,
    int Completed,
    int AwaitingMyAction,
    IReadOnlyDictionary<CaseStatus, int> ByStatus,
    IReadOnlyList<MonthCount> CreatedByMonth,
    IReadOnlyList<MonthCount> CompletedByMonth,
    IReadOnlyList<CaseSummaryDto> RecentCases,
    IReadOnlyList<CaseSummaryDto> MyTasks,
    IReadOnlyList<CaseSummaryDto> RequiringAttention,
    IReadOnlyList<CaseSummaryDto> RecentActivity,
    IReadOnlySet<CaseStatus> ActionableStatuses,
    int OverdueAfterDays,
    bool Partial,
    int Scanned,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Builds the case dashboard from <see cref="ICaseService.ListAsync"/> only, so the service's authorization applies to
/// every figure (a user without <c>cases.view</c> gets Forbidden, not numbers), and nothing is hard-coded. The case model
/// has no assignee, priority, category or due date, so the dashboard uses these documented definitions (ADR 0011):
/// <list type="bullet">
/// <item>Open: New, Triaged or In Progress. Pending review: Resolved (waiting to be closed). Completed: Resolved or Closed.</item>
/// <item>Overdue: open and not updated for <see cref="DashboardOptions.OverdueAfterDays"/> days.</item>
/// <item>Awaiting my action ("assigned to me"): in a status from which the signed-in user holds a transition
/// permission, using the domain workflow edges and the E3 permission set, the same rule that produces the buttons.</item>
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

    private DashboardOptions Settings => options.Value;

    public async Task<Result<CaseDashboard>> LoadAsync(CancellationToken ct)
    {
        var actionable = await ActionableStatusesAsync(ct);
        var snapshot = await ReadAllAsync(ct);
        if (snapshot.Error is { } error)
        {
            return Result<CaseDashboard>.Failure(error);
        }

        var all = snapshot.Items;
        var now = time.GetUtcNow();
        var overdueBefore = now.AddDays(-Settings.OverdueAfterDays);
        var byStatus = Enum.GetValues<CaseStatus>().ToDictionary(s => s, s => all.Count(c => c.Status == s));
        var open = all.Where(c => OpenStatuses.Contains(c.Status)).ToList();
        var overdue = open.Where(c => c.UpdatedAtUtc < overdueBefore).OrderBy(c => c.UpdatedAtUtc).ThenBy(c => c.Id).ToList();
        var mine = all.Where(c => actionable.Contains(c.Status)).OrderBy(c => c.UpdatedAtUtc).ThenBy(c => c.Id).ToList();
        var months = Months(now, Settings.TrendMonths);

        return Result<CaseDashboard>.Success(new CaseDashboard(
            Total: snapshot.TotalCount,
            Open: open.Count,
            PendingReview: byStatus[CaseStatus.Resolved],
            Overdue: overdue.Count,
            Completed: byStatus[CaseStatus.Resolved] + byStatus[CaseStatus.Closed],
            AwaitingMyAction: mine.Count,
            ByStatus: byStatus,
            CreatedByMonth: CountByMonth(months, all.Select(c => c.CreatedAtUtc)),
            // A case's completion time is approximated by its last update (Resolved cases can still be edited).
            CompletedByMonth: CountByMonth(months, all.Where(c => c.Status is CaseStatus.Resolved or CaseStatus.Closed).Select(c => c.UpdatedAtUtc)),
            RecentCases: [.. all.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id).Take(5)],
            MyTasks: [.. mine.Take(5)],
            RequiringAttention: [.. overdue.Take(5)],
            RecentActivity: [.. all.OrderByDescending(c => c.UpdatedAtUtc).ThenByDescending(c => c.Id).Take(5)],
            ActionableStatuses: actionable,
            OverdueAfterDays: Settings.OverdueAfterDays,
            Partial: snapshot.Items.Count < snapshot.TotalCount,
            Scanned: snapshot.Items.Count,
            GeneratedAtUtc: now));
    }

    /// <summary>
    /// Statuses from which the signed-in user may move a case: an edge of the domain workflow for which the user holds
    /// the transition permission. Mirrors how <c>CaseDetailDto.AllowedNextStatuses</c> is produced; no new rule.
    /// </summary>
    public async Task<IReadOnlySet<CaseStatus>> ActionableStatusesAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        return Enum.GetValues<CaseStatus>()
            .Where(from => workflow.NextStatuses(from).Any(to => actor.HasPermission(Permissions.Transition(from, to))))
            .ToHashSet();
    }

    /// <summary>Cases updated within the activity window, newest first (the Notifications page).</summary>
    public async Task<Result<IReadOnlyList<CaseSummaryDto>>> RecentActivityAsync(int max, CancellationToken ct)
    {
        var since = time.GetUtcNow().AddDays(-Settings.RecentActivityDays);
        var result = await cases.ListAsync(new CaseListQuery(1, Math.Clamp(max, 1, 100), SortBy: CaseSortField.UpdatedAt, Direction: SortDirection.Descending), ct);
        if (!result.IsSuccess)
        {
            return Result<IReadOnlyList<CaseSummaryDto>>.Failure(result.Error!);
        }
        return Result<IReadOnlyList<CaseSummaryDto>>.Success([.. result.Value!.Items.Where(c => c.UpdatedAtUtc >= since)]);
    }

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
