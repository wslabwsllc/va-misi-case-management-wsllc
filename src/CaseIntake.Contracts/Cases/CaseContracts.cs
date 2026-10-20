using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Cases;

/// <summary>New case. Without <paramref name="Triage"/> the case is Medium / General / unassigned; an empty due date
/// is set from the priority (<see cref="CaseTriageRules.DefaultDueOn"/>).</summary>
public sealed record CreateCaseRequest(string Title, string? Description, VeteranSnapshot? Veteran, CaseTriage? Triage = null);

/// <summary>Edit. A null <paramref name="Triage"/> leaves priority, category, owner and due date unchanged.</summary>
public sealed record UpdateCaseRequest(string Title, string? Description, uint Version, CaseTriage? Triage = null);   // Version = optimistic concurrency token
public sealed record ChangeStatusRequest(CaseStatus TargetStatus, uint Version);
public sealed record VeteranSnapshot(string Name, string Address, string SsnLast4);

public sealed record CaseSummaryDto(long Id, string Title, CaseStatus Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    CasePriority Priority = CasePriority.Medium, CaseCategory Category = CaseCategory.General,
    string? AssignedTo = null, DateOnly? DueOn = null);
public sealed record CaseDetailDto(long Id, string Title, string? Description, CaseStatus Status,
    DateTimeOffset CreatedAtUtc, string CreatedBy, DateTimeOffset UpdatedAtUtc, string UpdatedBy,
    uint Version, VeteranSnapshot? Veteran, IReadOnlyList<CaseStatus> AllowedNextStatuses,
    CasePriority Priority = CasePriority.Medium, CaseCategory Category = CaseCategory.General,
    string? AssignedTo = null, DateOnly? DueOn = null)
{
    public CaseTriage Triage => new(Priority, Category, AssignedTo, DueOn);
}
public sealed record AuditEntryDto(long Id, long CaseId, DateTimeOffset OccurredAtUtc, string Actor,
    string Action, CaseStatus? FromStatus, CaseStatus? ToStatus, string? Details);

public enum CaseSortField { Id, Title, Status, CreatedAt, UpdatedAt, Priority, DueOn, Category, Owner }
public enum SortDirection { Ascending, Descending }

/// <summary>
/// Case list query. Every filter is optional and they combine with AND. <paramref name="AssignedTo"/> matches the owner's
/// user name exactly (<see cref="UnassignedFilter"/> = no owner); <paramref name="OverdueOnly"/> keeps open cases whose
/// due date is before today (UTC); the date ranges are inclusive UTC instants.
/// </summary>
public sealed record CaseListQuery(int Page = 1, int PageSize = 25,
    IReadOnlyCollection<CaseStatus>? Statuses = null, string? Search = null,
    CaseSortField SortBy = CaseSortField.UpdatedAt, SortDirection Direction = SortDirection.Descending,
    IReadOnlyCollection<CasePriority>? Priorities = null, IReadOnlyCollection<CaseCategory>? Categories = null,
    string? AssignedTo = null, bool OverdueOnly = false,
    DateTimeOffset? CreatedFromUtc = null, DateTimeOffset? CreatedToUtc = null,
    DateTimeOffset? UpdatedFromUtc = null, DateTimeOffset? UpdatedToUtc = null)
{
    /// <summary>The <see cref="AssignedTo"/> value that selects cases without an owner.</summary>
    public const string UnassignedFilter = "(unassigned)";
}
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public interface ICaseService
{
    Task<Result<CaseDetailDto>> CreateAsync(CreateCaseRequest request, CancellationToken ct);
    Task<Result<CaseDetailDto>> GetAsync(long id, CancellationToken ct);
    Task<Result<CaseDetailDto>> UpdateAsync(long id, UpdateCaseRequest request, CancellationToken ct);
    Task<Result<CaseDetailDto>> ChangeStatusAsync(long id, ChangeStatusRequest request, CancellationToken ct);
    Task<Result<PagedResult<CaseSummaryDto>>> ListAsync(CaseListQuery query, CancellationToken ct);
    Task<Result<IReadOnlyList<AuditEntryDto>>> GetAuditTrailAsync(long id, CancellationToken ct);
}

/// <summary>A user who can own cases: holds a role that may edit cases and move them along the workflow.</summary>
public sealed record AssigneeDto(string UserName, string Role);

/// <summary>The people a case can be assigned to (issue #22). Requires <c>cases.view</c>.</summary>
public interface ICaseAssigneeDirectory
{
    Task<Result<IReadOnlyList<AssigneeDto>>> ListAsync(CancellationToken ct);
}

/// <summary>One entry of the case activity feed: something someone else did to a case the user can see.</summary>
public sealed record CaseNotificationDto(long Id, long CaseId, string CaseTitle, string Action,
    CaseStatus? FromStatus, CaseStatus? ToStatus, string Actor, DateTimeOffset OccurredAtUtc, string? Details,
    bool Unread, bool AssignedToMe, bool AssignedToYou = false);

/// <summary>The feed (newest first) and how many entries are unread.</summary>
public sealed record CaseNotificationFeed(IReadOnlyList<CaseNotificationDto> Items, int UnreadCount, DateTimeOffset? LastReadAtUtc);

/// <summary>
/// Case activity notifications (issue #21): audit activity by other users within the activity window, with a per-user
/// "read up to" time. Requires <c>cases.view</c>.
/// </summary>
public interface ICaseNotificationService
{
    Task<Result<CaseNotificationFeed>> GetFeedAsync(int max, CancellationToken ct);
    Task<Result<int>> GetUnreadCountAsync(CancellationToken ct);
    Task<Result<CaseNotificationFeed>> MarkAllReadAsync(CancellationToken ct);
}
