using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Cases;

/// <summary>
/// Case intake, detail, edit, list and status workflow (Epic 2). Each operation reads the actor once, checks its
/// permission, uses its own short-lived context and commits the business change and its audit entry in a single
/// SaveChanges. Queries return materialized DTOs.
/// </summary>
public sealed class CaseService(
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    ICaseWorkflow workflow,
    TimeProvider timeProvider,
    ILogger<CaseService> logger) : ICaseService
{
    public const int MaxPageSize = 100;

    public async Task<Result<CaseDetailDto>> CreateAsync(CreateCaseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesCreate))
        {
            return Denied<CaseDetailDto>(nameof(CreateAsync), actor);
        }
        if (CaseRequestValidator.Validate(request) is { } invalid)
        {
            return Rejected<CaseDetailDto>(nameof(CreateAsync), invalid);
        }

        var now = timeProvider.GetUtcNow();
        var veteran = request.Veteran is { } v ? new CaseVeteran(v.Name, v.Address, v.SsnLast4) : null;
        var entity = Case.Create(request.Title, request.Description, veteran, AuditAppender.ActorName(actor), now);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        db.Cases.Add(entity);
        AuditAppender.Append(db, entity, actor, AuditEvent.Created(now, veteran is not null));
        await db.SaveChangesAsync(ct);

        CaseLog.CaseCreated(logger, entity.Id, actor.UserId!);
        return Result<CaseDetailDto>.Success(ToDetail(entity, actor));
    }

    public async Task<Result<CaseDetailDto>> GetAsync(long id, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView))
        {
            return Denied<CaseDetailDto>(nameof(GetAsync), actor);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var entity = await db.Cases.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return entity is null
            ? Result<CaseDetailDto>.Failure(CaseErrors.NotFound)
            : Result<CaseDetailDto>.Success(ToDetail(entity, actor));
    }

    public async Task<Result<CaseDetailDto>> UpdateAsync(long id, UpdateCaseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesEdit))
        {
            return Denied<CaseDetailDto>(nameof(UpdateAsync), actor);
        }
        if (CaseRequestValidator.Validate(request) is { } invalid)
        {
            return Rejected<CaseDetailDto>(nameof(UpdateAsync), invalid);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var entity = await db.Cases.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return Result<CaseDetailDto>.Failure(CaseErrors.NotFound);
        }
        if (entity.IsClosed)
        {
            return Rejected<CaseDetailDto>(nameof(UpdateAsync), CaseErrors.CaseClosed);
        }
        if (entity.Version != request.Version)
        {
            CaseLog.VersionConflict(logger, id, nameof(UpdateAsync));
            return Result<CaseDetailDto>.Failure(CaseErrors.Conflict);
        }

        var now = timeProvider.GetUtcNow();
        var changed = entity.Edit(request.Title, request.Description, AuditAppender.ActorName(actor), now);
        if (changed.Count == 0)
        {
            // No-op edit: no timestamp, version or audit change.
            return Result<CaseDetailDto>.Success(ToDetail(entity, actor));
        }

        AuditAppender.Append(db, entity, actor, AuditEvent.Edited(now, changed));
        if (!await TrySaveWithVersionAsync(db, entity, request.Version, nameof(UpdateAsync), ct))
        {
            return Result<CaseDetailDto>.Failure(CaseErrors.Conflict);
        }

        CaseLog.CaseUpdated(logger, id, actor.UserId!, string.Join(",", changed));
        return Result<CaseDetailDto>.Success(ToDetail(entity, actor));
    }

    public async Task<Result<CaseDetailDto>> ChangeStatusAsync(long id, ChangeStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView))
        {
            return Denied<CaseDetailDto>(nameof(ChangeStatusAsync), actor);
        }
        if (!Enum.IsDefined(request.TargetStatus))
        {
            return Rejected<CaseDetailDto>(nameof(ChangeStatusAsync), CaseErrors.InvalidStatus);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var entity = await db.Cases.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return Result<CaseDetailDto>.Failure(CaseErrors.NotFound);
        }

        var from = entity.Status;
        if (!workflow.IsAllowed(from, request.TargetStatus))
        {
            return Rejected<CaseDetailDto>(nameof(ChangeStatusAsync), CaseErrors.InvalidTransition);
        }
        if (!actor.HasPermission(Permissions.Transition(from, request.TargetStatus)))
        {
            return Denied<CaseDetailDto>(nameof(ChangeStatusAsync), actor);
        }
        if (entity.Version != request.Version)
        {
            CaseLog.VersionConflict(logger, id, nameof(ChangeStatusAsync));
            return Result<CaseDetailDto>.Failure(CaseErrors.Conflict);
        }

        var now = timeProvider.GetUtcNow();
        entity.TransitionTo(request.TargetStatus, workflow, AuditAppender.ActorName(actor), now);
        AuditAppender.Append(db, entity, actor, AuditEvent.StatusChange(now, from, request.TargetStatus));
        if (!await TrySaveWithVersionAsync(db, entity, request.Version, nameof(ChangeStatusAsync), ct))
        {
            return Result<CaseDetailDto>.Failure(CaseErrors.Conflict);
        }

        CaseLog.CaseStatusChanged(logger, id, from.ToString(), request.TargetStatus.ToString(), actor.UserId!);
        return Result<CaseDetailDto>.Success(ToDetail(entity, actor));
    }

    public async Task<Result<PagedResult<CaseSummaryDto>>> ListAsync(CaseListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView))
        {
            return Denied<PagedResult<CaseSummaryDto>>(nameof(ListAsync), actor);
        }
        if (CaseListQueryValidator.Validate(query) is { } invalid)
        {
            return Rejected<PagedResult<CaseSummaryDto>>(nameof(ListAsync), invalid);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var filtered = CaseListQueryBuilder.Filter(db.Cases.AsNoTracking(), query);
        var total = await filtered.CountAsync(ct);
        var items = await CaseListQueryBuilder.Sort(filtered, query.SortBy, query.Direction)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CaseSummaryDto(c.Id, c.Title, c.Status, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(ct);

        return Result<PagedResult<CaseSummaryDto>>.Success(new PagedResult<CaseSummaryDto>(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<AuditEntryDto>>> GetAuditTrailAsync(long id, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView))
        {
            return Denied<IReadOnlyList<AuditEntryDto>>(nameof(GetAuditTrailAsync), actor);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        if (!await db.Cases.AnyAsync(c => c.Id == id, ct))
        {
            return Result<IReadOnlyList<AuditEntryDto>>.Failure(CaseErrors.NotFound);
        }

        var entries = await db.CaseAuditEntries.AsNoTracking()
            .Where(a => a.CaseId == id)
            .OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id)
            .Select(a => new AuditEntryDto(a.Id, a.CaseId, a.OccurredAtUtc, a.Actor, a.Action, a.FromStatus, a.ToStatus, a.Details))
            .ToListAsync(ct);
        return Result<IReadOnlyList<AuditEntryDto>>.Success(entries);
    }

    /// <summary>Statuses the actor may move the case to: workflow edges filtered by the actor's transition permissions.</summary>
    internal static IReadOnlyList<CaseStatus> AllowedNextStatuses(CaseStatus status, OperationActor actor, ICaseWorkflow workflow) =>
        [.. workflow.NextStatuses(status).Where(target => actor.HasPermission(Permissions.Transition(status, target)))];

    private CaseDetailDto ToDetail(Case c, OperationActor actor) => new(
        c.Id, c.Title, c.Description, c.Status, c.CreatedAtUtc, c.CreatedBy, c.UpdatedAtUtc, c.UpdatedBy, c.Version,
        c.Veteran is { } v ? new VeteranSnapshot(v.Name, v.Address, v.SsnLast4) : null,
        AllowedNextStatuses(c.Status, actor, workflow));

    /// <summary>
    /// Saves with the version the caller last saw as the concurrency token's original value, so a stale browser form
    /// is detected even though the entity was freshly loaded. A concurrent commit since loading also surfaces here.
    /// </summary>
    private async Task<bool> TrySaveWithVersionAsync(AppDbContext db, Case entity, uint submittedVersion, string operation, CancellationToken ct)
    {
        db.Entry(entity).Property(c => c.Version).OriginalValue = submittedVersion;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            CaseLog.VersionConflict(logger, entity.Id, operation);
            return false;
        }
    }

    private Result<T> Denied<T>(string operation, OperationActor actor)
    {
        CaseLog.OperationDenied(logger, operation, actor.UserId);
        return Result<T>.Failure(CaseErrors.Forbidden);
    }

    private Result<T> Rejected<T>(string operation, Error error)
    {
        CaseLog.RequestRejected(logger, operation, error.Code);
        return Result<T>.Failure(error);
    }
}
