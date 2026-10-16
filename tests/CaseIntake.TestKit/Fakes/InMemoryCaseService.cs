using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;

namespace CaseIntake.TestKit.Fakes;

/// <summary>
/// In-memory <see cref="ICaseService"/> so the UI can be built and component-tested before the real module exists.
/// It is deliberately thin (length checks, the five allowed edges, version bump); the real rules live in the Cases
/// module. Never registered in the composed application.
/// </summary>
public sealed class InMemoryCaseService(ICurrentUser currentUser, TimeProvider time) : ICaseService
{
    private static readonly Dictionary<CaseStatus, CaseStatus[]> Edges = new()
    {
        [CaseStatus.New] = [CaseStatus.Triaged],
        [CaseStatus.Triaged] = [CaseStatus.InProgress],
        [CaseStatus.InProgress] = [CaseStatus.Resolved],
        [CaseStatus.Resolved] = [CaseStatus.Closed, CaseStatus.InProgress],
    };

    private readonly List<Row> _cases = [];
    private readonly List<AuditEntryDto> _audit = [];

    public async Task<Result<CaseDetailDto>> CreateAsync(CreateCaseRequest request, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesCreate)) return Forbidden<CaseDetailDto>();
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 5 or > 200) return TitleInvalid<CaseDetailDto>();

        var now = time.GetUtcNow();
        var row = new Row(_cases.Count + 1, title, request.Description, CaseStatus.New, now, actor.UserId!, now, actor.UserId!, 1, request.Veteran);
        _cases.Add(row);
        Audit(row.Id, actor, "Created", null, CaseStatus.New);
        return Result<CaseDetailDto>.Success(ToDetail(row, actor));
    }

    public async Task<Result<CaseDetailDto>> GetAsync(long id, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView)) return Forbidden<CaseDetailDto>();
        var row = _cases.FirstOrDefault(c => c.Id == id);
        return row is null ? NotFound<CaseDetailDto>() : Result<CaseDetailDto>.Success(ToDetail(row, actor));
    }

    public async Task<Result<CaseDetailDto>> UpdateAsync(long id, UpdateCaseRequest request, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesEdit)) return Forbidden<CaseDetailDto>();
        var row = _cases.FirstOrDefault(c => c.Id == id);
        if (row is null) return NotFound<CaseDetailDto>();
        if (row.Status == CaseStatus.Closed) return Result<CaseDetailDto>.Failure(new Error(ErrorKind.Validation, "CaseClosed", "Closed cases cannot be edited."));
        if (row.Version != request.Version) return Conflict<CaseDetailDto>();
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 5 or > 200) return TitleInvalid<CaseDetailDto>();

        var updated = row with { Title = title, Description = request.Description, UpdatedAtUtc = time.GetUtcNow(), UpdatedBy = actor.UserId!, Version = row.Version + 1 };
        _cases[_cases.IndexOf(row)] = updated;
        Audit(id, actor, "Edited", null, null);
        return Result<CaseDetailDto>.Success(ToDetail(updated, actor));
    }

    public async Task<Result<CaseDetailDto>> ChangeStatusAsync(long id, ChangeStatusRequest request, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        var row = _cases.FirstOrDefault(c => c.Id == id);
        if (row is null) return NotFound<CaseDetailDto>();
        if (!Edges.TryGetValue(row.Status, out var next) || !next.Contains(request.TargetStatus))
            return Result<CaseDetailDto>.Failure(new Error(ErrorKind.InvalidTransition, "InvalidTransition", "Transition not allowed."));
        if (!actor.HasPermission(Permissions.Transition(row.Status, request.TargetStatus))) return Forbidden<CaseDetailDto>();
        if (row.Version != request.Version) return Conflict<CaseDetailDto>();

        var updated = row with { Status = request.TargetStatus, UpdatedAtUtc = time.GetUtcNow(), UpdatedBy = actor.UserId!, Version = row.Version + 1 };
        _cases[_cases.IndexOf(row)] = updated;
        Audit(id, actor, row.Status == CaseStatus.Resolved && request.TargetStatus == CaseStatus.InProgress ? "Reopened" : "StatusChanged", row.Status, request.TargetStatus);
        return Result<CaseDetailDto>.Success(ToDetail(updated, actor));
    }

    public async Task<Result<PagedResult<CaseSummaryDto>>> ListAsync(CaseListQuery query, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView)) return Forbidden<PagedResult<CaseSummaryDto>>();
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            return Result<PagedResult<CaseSummaryDto>>.Failure(new Error(ErrorKind.Validation, "InvalidPaging", "Invalid paging."));

        IEnumerable<Row> rows = _cases;
        if (query.Statuses is { Count: > 0 }) rows = rows.Where(r => query.Statuses.Contains(r.Status));
        if (!string.IsNullOrWhiteSpace(query.Search))
            rows = rows.Where(r => r.Title.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                                   || (r.Description?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false));
        Func<Row, object> key = query.SortBy switch
        {
            CaseSortField.Id => r => r.Id,
            CaseSortField.Title => r => r.Title,
            CaseSortField.Status => r => (int)r.Status,
            CaseSortField.CreatedAt => r => r.CreatedAtUtc,
            _ => r => r.UpdatedAtUtc,
        };
        var ordered = query.Direction == SortDirection.Ascending ? rows.OrderBy(key).ThenBy(r => r.Id) : rows.OrderByDescending(key).ThenBy(r => r.Id);
        var list = ordered.ToList();
        var page = list.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(r => new CaseSummaryDto(r.Id, r.Title, r.Status, r.CreatedAtUtc, r.UpdatedAtUtc)).ToList();
        return Result<PagedResult<CaseSummaryDto>>.Success(new PagedResult<CaseSummaryDto>(page, query.Page, query.PageSize, list.Count));
    }

    public async Task<Result<IReadOnlyList<AuditEntryDto>>> GetAuditTrailAsync(long id, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView)) return Forbidden<IReadOnlyList<AuditEntryDto>>();
        return Result<IReadOnlyList<AuditEntryDto>>.Success([.. _audit.Where(a => a.CaseId == id)]);
    }

    private void Audit(long caseId, OperationActor actor, string action, CaseStatus? from, CaseStatus? to) =>
        _audit.Add(new AuditEntryDto(_audit.Count + 1, caseId, time.GetUtcNow(), actor.UserId!, action, from, to, null));

    private static CaseDetailDto ToDetail(Row r, OperationActor actor) => new(
        r.Id, r.Title, r.Description, r.Status, r.CreatedAtUtc, r.CreatedBy, r.UpdatedAtUtc, r.UpdatedBy, r.Version, r.Veteran,
        Edges.TryGetValue(r.Status, out var next) ? [.. next.Where(t => actor.HasPermission(Permissions.Transition(r.Status, t)))] : []);

    private static Result<T> Forbidden<T>() => Result<T>.Failure(new Error(ErrorKind.Forbidden, "Forbidden", "Not allowed."));
    private static Result<T> NotFound<T>() => Result<T>.Failure(new Error(ErrorKind.NotFound, "CaseNotFound", "Case not found."));
    private static Result<T> Conflict<T>() => Result<T>.Failure(new Error(ErrorKind.Conflict, "VersionConflict", "The case was changed by someone else."));
    private static Result<T> TitleInvalid<T>() => Result<T>.Failure(new Error(ErrorKind.Validation, "Validation", "Invalid title.",
        new Dictionary<string, string[]> { ["Title"] = ["Title must be 5 to 200 characters."] }));

    private sealed record Row(long Id, string Title, string? Description, CaseStatus Status, DateTimeOffset CreatedAtUtc,
        string CreatedBy, DateTimeOffset UpdatedAtUtc, string UpdatedBy, uint Version, VeteranSnapshot? Veteran);
}
