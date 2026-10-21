using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Diagnostics;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using CaseIntake.Persistence.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Diagnostics;

/// <summary>
/// The stored error log (ADR 0015): reading needs <c>errors.view</c>, status changes <c>errors.manage</c>. A status change
/// is checked against the row version (a stale one is a Conflict) and recorded in the audit log in the same save.
/// </summary>
internal sealed class ErrorLogService(
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<ErrorLogService> logger) : IErrorLogService
{
    private const string LikeEscape = "\\";

    private static Error Forbidden { get; } =
        new(ErrorKind.Forbidden, "Forbidden", "You do not have permission to perform this action.");

    private static Error NotFound { get; } = new(ErrorKind.NotFound, "ErrorNotFound", "This error does not exist.");

    public async Task<Result<PagedResult<ErrorSummaryDto>>> ListAsync(ErrorQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await AuthorizeAsync(Permissions.ErrorsView, "ListErrors", ct) is null)
        {
            return Result<PagedResult<ErrorSummaryDto>>.Failure(Forbidden);
        }
        if (query.FromDate is { } f && query.ToDate is { } t && f > t)
        {
            return Result<PagedResult<ErrorSummaryDto>>.Failure(new(ErrorKind.Validation, "Validation", "Some filters need attention.",
                new Dictionary<string, string[]> { [nameof(ErrorQuery.ToDate)] = ["The end date must be on or after the start date."] }));
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, ErrorQuery.MaxPageSize);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var q = db.ErrorLogEntries.AsNoTracking();
        if (query.FromDate is { } from) { var start = EasternTime.StartOfDay(from); q = q.Where(e => e.OccurredAtUtc >= start); }
        if (query.ToDate is { } to) { var end = EasternTime.EndOfDay(to); q = q.Where(e => e.OccurredAtUtc <= end); }
        if (query.Level is { } level) q = q.Where(e => e.Level == level);
        if (query.Status is { } status) q = q.Where(e => e.Status == status);
        if (query.Fingerprint is { Length: > 0 } fingerprint) q = q.Where(e => e.Fingerprint == fingerprint);
        if (query.Search?.Trim() is { Length: > 0 } text)
        {
            var p = "%" + text[..Math.Min(text.Length, ErrorQuery.SearchMaxLength)]
                .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
                .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
                .Replace("_", LikeEscape + "_", StringComparison.Ordinal) + "%";
            q = q.Where(e => EF.Functions.ILike(e.Message, p, LikeEscape) || EF.Functions.ILike(e.Category, p, LikeEscape)
                || (e.ExceptionType != null && EF.Functions.ILike(e.ExceptionType, p, LikeEscape))
                || (e.RequestPath != null && EF.Functions.ILike(e.RequestPath, p, LikeEscape))
                || (e.CorrelationId != null && EF.Functions.ILike(e.CorrelationId, p, LikeEscape)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return Result<PagedResult<ErrorSummaryDto>>.Success(new([.. rows.Select(Summary)], page, size, total));
    }

    public async Task<Result<ErrorDetailDto>> GetAsync(long id, CancellationToken ct)
    {
        if (await AuthorizeAsync(Permissions.ErrorsView, "GetError", ct) is null)
        {
            return Result<ErrorDetailDto>.Failure(Forbidden);
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await DetailAsync(db, id, ct) is { } detail ? Result<ErrorDetailDto>.Success(detail) : Result<ErrorDetailDto>.Failure(NotFound);
    }

    public async Task<Result<IReadOnlyList<ErrorSummaryDto>>> FindByReferenceAsync(string reference, CancellationToken ct)
    {
        if (await AuthorizeAsync(Permissions.ErrorsView, "FindErrorByReference", ct) is null)
        {
            return Result<IReadOnlyList<ErrorSummaryDto>>.Failure(Forbidden);
        }
        var id = reference?.Trim() ?? string.Empty;
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var rows = id.Length == 0 ? [] : await db.ErrorLogEntries.AsNoTracking().Where(e => e.CorrelationId == id)
            .OrderBy(e => e.OccurredAtUtc).Take(50).ToListAsync(ct);
        return Result<IReadOnlyList<ErrorSummaryDto>>.Success([.. rows.Select(Summary)]);
    }

    public async Task<Result<ErrorDetailDto>> SetStatusAsync(long id, SetErrorStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AuthorizeAsync(Permissions.ErrorsManage, "SetErrorStatus", ct) is not { } actor)
        {
            return Result<ErrorDetailDto>.Failure(Forbidden);
        }
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > SetErrorStatusRequest.NoteMaxLength } || !Enum.IsDefined(request.Status))
        {
            return Result<ErrorDetailDto>.Failure(new(ErrorKind.Validation, "Validation", "Some fields need attention.",
                new Dictionary<string, string[]> { ["Note"] = [$"Use at most {SetErrorStatusRequest.NoteMaxLength} characters."] }));
        }

        using var operation = AuditOperation.BeginIfNone(logger, "SetErrorStatus");
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var entry = await db.ErrorLogEntries.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
        {
            return Result<ErrorDetailDto>.Failure(NotFound);
        }
        // The caller's version becomes the original value, so a change saved in between is detected.
        db.Entry(entry).Property(e => e.Version).OriginalValue = (uint)request.Version;
        var before = entry.Status;
        entry.Status = request.Status;
        entry.StatusChangedUtc = time.GetUtcNow();
        entry.StatusChangedBy = AuditAppender.ActorName(actor);
        entry.StatusNote = note;
        SystemAudit.Append(db, new AuditRecord(AuditCategories.Errors, AuditLogActions.ErrorStatusChanged, AuditOutcome.Success,
            actor.UserId, AuditAppender.ActorName(actor), AuditEntityTypes.Error, id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            entry.ExceptionType ?? entry.Category, $"Status {before} → {request.Status}",
            new Dictionary<string, string?> { ["Status"] = before.ToString() },
            new Dictionary<string, string?> { ["Status"] = request.Status.ToString(), ["Note"] = note }), time.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ErrorDetailDto>.Failure(new(ErrorKind.Conflict, "Conflict",
                "Someone else changed this error while you were looking at it. The latest version is shown."));
        }

        await using var fresh = await contextFactory.CreateDbContextAsync(ct);
        return await DetailAsync(fresh, id, ct) is { } detail ? Result<ErrorDetailDto>.Success(detail) : Result<ErrorDetailDto>.Failure(NotFound);
    }

    private async Task<OperationActor?> AuthorizeAsync(string permission, string operation, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (actor.HasPermission(permission))
        {
            return actor;
        }
        IdentityLog.AuthorizationDenied(logger, actor.UserId, permission, operation);
        return null;
    }

    private static async Task<ErrorDetailDto?> DetailAsync(AppDbContext db, long id, CancellationToken ct)
    {
        var e = await db.ErrorLogEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
        {
            return null;
        }
        var occurrences = await db.ErrorLogEntries.CountAsync(x => x.Fingerprint == e.Fingerprint, ct);
        return new ErrorDetailDto(e.Id, e.OccurredAtUtc, e.Level, e.Category, e.EventId, e.EventName, e.Message, e.ExceptionType,
            e.StackTrace, e.CorrelationId, e.RequestPath, e.Fingerprint, occurrences, e.Status, e.StatusChangedUtc, e.StatusChangedBy,
            e.StatusNote, e.Version);
    }

    internal static ErrorSummaryDto Summary(ErrorLogEntry e) =>
        new(e.Id, e.OccurredAtUtc, e.Level, e.Category, e.Message, e.ExceptionType, e.Status, e.CorrelationId, e.Fingerprint);
}
