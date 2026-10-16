using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Cases;

public sealed record CreateCaseRequest(string Title, string? Description, VeteranSnapshot? Veteran);
public sealed record UpdateCaseRequest(string Title, string? Description, uint Version);   // Version = optimistic concurrency token
public sealed record ChangeStatusRequest(CaseStatus TargetStatus, uint Version);
public sealed record VeteranSnapshot(string Name, string Address, string SsnLast4);

public sealed record CaseSummaryDto(long Id, string Title, CaseStatus Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CaseDetailDto(long Id, string Title, string? Description, CaseStatus Status,
    DateTimeOffset CreatedAtUtc, string CreatedBy, DateTimeOffset UpdatedAtUtc, string UpdatedBy,
    uint Version, VeteranSnapshot? Veteran, IReadOnlyList<CaseStatus> AllowedNextStatuses);
public sealed record AuditEntryDto(long Id, long CaseId, DateTimeOffset OccurredAtUtc, string Actor,
    string Action, CaseStatus? FromStatus, CaseStatus? ToStatus, string? Details);

public enum CaseSortField { Id, Title, Status, CreatedAt, UpdatedAt }
public enum SortDirection { Ascending, Descending }
public sealed record CaseListQuery(int Page = 1, int PageSize = 25,
    IReadOnlyCollection<CaseStatus>? Statuses = null, string? Search = null,
    CaseSortField SortBy = CaseSortField.UpdatedAt, SortDirection Direction = SortDirection.Descending);
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
