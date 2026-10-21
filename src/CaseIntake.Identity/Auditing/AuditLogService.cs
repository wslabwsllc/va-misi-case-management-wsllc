using System.Globalization;
using System.Text;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Auditing;

/// <summary>
/// The system-wide audit log viewer (ADR 0014). Reads three append-only sources: <c>AuditLogEntries</c>
/// (authentication, authorization and administration), <c>CaseAuditEntries</c> (cases and attachments) and
/// <c>TestResultAuditEntries</c>. Each source is filtered and ordered in SQL and limited to the rows the requested page
/// needs; the sources are then merged by time. That avoids a SQL UNION over tables whose columns differ in type, and
/// caps how deep a page may go at <see cref="AuditQuery.MaxExportRows"/>.
/// Public so the HTTP export endpoint can bind it to the request's actor.
/// </summary>
public sealed partial class AuditLogService(
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    AuditLogWriter writer,
    TimeProvider time,
    ILogger<AuditLogService> logger) : IAuditLogService
{
    private const string LikeEscape = "\\";
    private const string SystemPrefix = "A-", CasePrefix = "C-", TestPrefix = "T-";

    public async Task<Result<PagedResult<AuditLogEntryDto>>> ListAsync(AuditQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await AuthorizeAsync("ListAuditLog", ct) is null)
        {
            return Result<PagedResult<AuditLogEntryDto>>.Failure(Forbidden);
        }
        if (Validate(query) is { } invalid)
        {
            return Result<PagedResult<AuditLogEntryDto>>.Failure(invalid);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, AuditQuery.MaxPageSize);
        if ((long)page * size > AuditQuery.MaxExportRows)
        {
            return Result<PagedResult<AuditLogEntryDto>>.Failure(new(ErrorKind.Validation, "TooDeep",
                $"Only the first {AuditQuery.MaxExportRows:N0} matching entries can be paged through. Narrow the dates or filters to see older ones."));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var (rows, total) = await QueryAsync(db, query, page * size, ct);
        return Result<PagedResult<AuditLogEntryDto>>.Success(new PagedResult<AuditLogEntryDto>(
            [.. rows.Skip((page - 1) * size).Take(size)], page, size, total));
    }

    public async Task<Result<AuditLogEntryDto>> GetAsync(string key, CancellationToken ct)
    {
        if (await AuthorizeAsync("GetAuditEntry", ct) is null)
        {
            return Result<AuditLogEntryDto>.Failure(Forbidden);
        }

        var notFound = Result<AuditLogEntryDto>.Failure(new(ErrorKind.NotFound, "AuditEntryNotFound", "This audit entry does not exist."));
        if (key is not { Length: > 2 } || !long.TryParse(key.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return notFound;
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        AuditLogEntryDto? entry = key[..2] switch
        {
            SystemPrefix => (await db.AuditLogEntries.AsNoTracking().Where(a => a.Id == id).ToListAsync(ct)).Select(FromSystem).SingleOrDefault(),
            CasePrefix => (await db.CaseAuditEntries.AsNoTracking().Where(a => a.Id == id).ToListAsync(ct)).Select(FromCase).SingleOrDefault(),
            TestPrefix => (await db.TestResultAuditEntries.AsNoTracking().Where(a => a.Id == id).ToListAsync(ct)).Select(FromTest).SingleOrDefault(),
            _ => null,
        };
        return entry is null ? notFound : Result<AuditLogEntryDto>.Success(entry);
    }

    public async Task<Result<AuditExport>> ExportAsync(AuditQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await AuthorizeAsync("ExportAuditLog", ct) is not { } actor)
        {
            return Result<AuditExport>.Failure(Forbidden);
        }
        if (Validate(query) is { } invalid)
        {
            return Result<AuditExport>.Failure(invalid);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var (rows, total) = await QueryAsync(db, query with { OldestFirst = false }, AuditQuery.MaxExportRows, ct);
        var csv = new StringBuilder();
        csv.AppendLine("Key,OccurredAtUtc,Actor,ActorId,Category,Action,Outcome,EntityType,EntityId,EntityName,Summary,Changes,CorrelationId");
        foreach (var r in rows)
        {
            csv.Append(CsvText.Cell(r.Key)).Append(',')
               .Append(r.OccurredAtUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(',')
               .Append(CsvText.Cell(r.ActorName)).Append(',')
               .Append(CsvText.Cell(r.ActorId)).Append(',')
               .Append(CsvText.Cell(r.Category)).Append(',')
               .Append(CsvText.Cell(r.Action)).Append(',')
               .Append(r.Outcome).Append(',')
               .Append(CsvText.Cell(r.EntityType)).Append(',')
               .Append(CsvText.Cell(r.EntityId)).Append(',')
               .Append(CsvText.Cell(r.EntityName)).Append(',')
               .Append(CsvText.Cell(r.Summary)).Append(',')
               .Append(CsvText.Cell(string.Join("; ", r.Changes.Select(c => $"{c.Field}: {c.Before ?? "(none)"} -> {c.After ?? "(none)"}")))).Append(',')
               .Append(CsvText.Cell(r.CorrelationId)).AppendLine();
        }

        var truncated = total > rows.Count;
        // Exporting the audit log is itself audited, with the filters (no values typed into cases).
        await writer.WriteAsync(new AuditRecord(AuditCategories.AuditLog, AuditLogActions.AuditExported, AuditOutcome.Success,
            actor.UserId, AuditAppender.ActorName(actor), Summary: $"Exported {rows.Count:N0} of {total:N0} entries",
            After: Describe(query)), ct);
        Exported(logger, rows.Count, actor.UserId);
        var fileName = $"audit-log-{time.GetUtcNow():yyyyMMdd-HHmmss}Z.csv";
        return Result<AuditExport>.Success(new AuditExport(fileName,
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())], rows.Count, truncated));
    }

    private static Error Forbidden { get; } =
        new(ErrorKind.Forbidden, "Forbidden", "You do not have permission to perform this action.");

    private async Task<OperationActor?> AuthorizeAsync(string operation, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (actor.HasPermission(Permissions.AuditView))
        {
            return actor;
        }
        IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.AuditView, operation);
        return null;
    }

    private static Error? Validate(AuditQuery query)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.FromDate is { } from && query.ToDate is { } to && from > to)
        {
            errors[nameof(AuditQuery.ToDate)] = ["The end date must be on or after the start date."];
        }
        if (query.Search is { Length: > AuditQuery.SearchMaxLength } || query.Actor is { Length: > AuditQuery.SearchMaxLength })
        {
            errors[nameof(AuditQuery.Search)] = [$"Use at most {AuditQuery.SearchMaxLength} characters."];
        }
        if (query.Category is { } category && !AuditCategories.All.Contains(category))
        {
            errors[nameof(AuditQuery.Category)] = ["Choose a category from the list."];
        }
        return errors.Count == 0 ? null : new Error(ErrorKind.Validation, "Validation", "Some filters need attention.", errors);
    }

    /// <summary>The first <paramref name="limit"/> matching entries of all sources in the requested order, and the total match count.</summary>
    private static async Task<(List<AuditLogEntryDto> Rows, int Total)> QueryAsync(AppDbContext db, AuditQuery query, int limit, CancellationToken ct)
    {
        var fromUtc = query.FromDate is { } from ? EasternTime.StartOfDay(from) : (DateTimeOffset?)null;
        var toUtc = query.ToDate is { } to ? EasternTime.EndOfDay(to) : (DateTimeOffset?)null;
        var actor = Pattern(query.Actor);
        var search = Pattern(query.Search);
        var rows = new List<AuditLogEntryDto>();
        var total = 0;

        // System audit log.
        {
            var q = db.AuditLogEntries.AsNoTracking();
            if (fromUtc is { } f) q = q.Where(a => a.OccurredAtUtc >= f);
            if (toUtc is { } t) q = q.Where(a => a.OccurredAtUtc <= t);
            if (query.Category is { } c) q = q.Where(a => a.Category == c);
            if (actor is { } p) q = q.Where(a => a.ActorName != null && EF.Functions.ILike(a.ActorName, p, LikeEscape));
            if (query.EntityType is { } et) q = q.Where(a => a.EntityType == et);
            if (query.EntityId is { } ei) q = q.Where(a => a.EntityId == ei);
            if (query.Outcome is { } o) q = q.Where(a => a.Outcome == o);
            if (search is { } s)
            {
                q = q.Where(a => EF.Functions.ILike(a.Action, s, LikeEscape)
                    || (a.EntityName != null && EF.Functions.ILike(a.EntityName, s, LikeEscape))
                    || (a.EntityId != null && EF.Functions.ILike(a.EntityId, s, LikeEscape))
                    || (a.Summary != null && EF.Functions.ILike(a.Summary, s, LikeEscape))
                    || EF.Functions.ILike(a.CorrelationId, s, LikeEscape));
            }
            total += await q.CountAsync(ct);
            q = query.OldestFirst ? q.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id) : q.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id);
            rows.AddRange((await q.Take(limit).ToListAsync(ct)).Select(FromSystem));
        }

        // Case trail: always successful, about one case; attachments are their own category.
        var includeCases = query.Outcome is null or AuditOutcome.Success
            && query.Category is null or AuditCategories.Cases or AuditCategories.Attachments
            && query.EntityType is null or AuditEntityTypes.Case;
        long? caseId = query.EntityId is null ? null : long.TryParse(query.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var cid) ? cid : -1;
        if (includeCases)
        {
            var q = db.CaseAuditEntries.AsNoTracking();
            if (fromUtc is { } f) q = q.Where(a => a.OccurredAtUtc >= f);
            if (toUtc is { } t) q = q.Where(a => a.OccurredAtUtc <= t);
            if (query.Category == AuditCategories.Attachments) q = q.Where(a => a.Action == AuditActions.AttachmentUploaded);
            if (query.Category == AuditCategories.Cases) q = q.Where(a => a.Action != AuditActions.AttachmentUploaded);
            if (actor is { } p) q = q.Where(a => EF.Functions.ILike(a.Actor, p, LikeEscape));
            if (caseId is { } id) q = q.Where(a => a.CaseId == id);
            if (search is { } s)
            {
                var number = long.TryParse(query.Search!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
                q = q.Where(a => EF.Functions.ILike(a.Action, s, LikeEscape)
                    || (a.Details != null && EF.Functions.ILike(a.Details, s, LikeEscape))
                    || (a.CorrelationId != null && EF.Functions.ILike(a.CorrelationId, s, LikeEscape))
                    || a.CaseId == number);
            }
            total += await q.CountAsync(ct);
            q = query.OldestFirst ? q.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id) : q.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id);
            rows.AddRange((await q.Take(limit).ToListAsync(ct)).Select(FromCase));
        }

        // Test results trail: always successful; about a run when it has one.
        var includeTests = query.Outcome is null or AuditOutcome.Success
            && query.Category is null or AuditCategories.TestResults
            && query.EntityType is null or AuditEntityTypes.TestRun;
        if (includeTests)
        {
            var q = db.TestResultAuditEntries.AsNoTracking();
            if (fromUtc is { } f) q = q.Where(a => a.OccurredAtUtc >= f);
            if (toUtc is { } t) q = q.Where(a => a.OccurredAtUtc <= t);
            if (actor is { } p) q = q.Where(a => EF.Functions.ILike(a.Actor, p, LikeEscape));
            if (caseId is { } id) q = q.Where(a => a.RunId == id);
            if (search is { } s)
            {
                q = q.Where(a => EF.Functions.ILike(a.Action, s, LikeEscape)
                    || (a.Details != null && EF.Functions.ILike(a.Details, s, LikeEscape))
                    || (a.CorrelationId != null && EF.Functions.ILike(a.CorrelationId, s, LikeEscape)));
            }
            total += await q.CountAsync(ct);
            q = query.OldestFirst ? q.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id) : q.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id);
            rows.AddRange((await q.Take(limit).ToListAsync(ct)).Select(FromTest));
        }

        var ordered = query.OldestFirst
            ? rows.OrderBy(r => r.OccurredAtUtc).ThenBy(r => r.Key, StringComparer.Ordinal)
            : rows.OrderByDescending(r => r.OccurredAtUtc).ThenByDescending(r => r.Key, StringComparer.Ordinal);
        return ([.. ordered.Take(limit)], total);
    }

    private static string? Pattern(string? text) =>
        text?.Trim() is { Length: > 0 } t
            ? "%" + t.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
                     .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
                     .Replace("_", LikeEscape + "_", StringComparison.Ordinal) + "%"
            : null;

    internal static AuditLogEntryDto FromSystem(AuditLogEntry a)
    {
        var before = AuditLogEntry.Deserialize(a.Before);
        var after = AuditLogEntry.Deserialize(a.After);
        var changes = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal)
            .Select(k => new AuditChangeDto(k, before.GetValueOrDefault(k), after.GetValueOrDefault(k))).ToList();
        return new AuditLogEntryDto(SystemPrefix + a.Id.ToString(CultureInfo.InvariantCulture), a.OccurredAtUtc, a.ActorId, a.ActorName,
            a.Category, a.Action, a.Outcome, a.EntityType, a.EntityId, a.EntityName, a.Summary, a.CorrelationId, changes);
    }

    internal static AuditLogEntryDto FromCase(CaseAuditEntry a)
    {
        var id = a.CaseId.ToString(CultureInfo.InvariantCulture);
        IReadOnlyList<AuditChangeDto> changes = a.FromStatus is null && a.ToStatus is null
            ? []
            : [new AuditChangeDto("Status", StatusText(a.FromStatus), StatusText(a.ToStatus))];
        return new AuditLogEntryDto(CasePrefix + a.Id.ToString(CultureInfo.InvariantCulture), a.OccurredAtUtc, a.ActorId, a.Actor,
            a.Action == AuditActions.AttachmentUploaded ? AuditCategories.Attachments : AuditCategories.Cases, a.Action,
            AuditOutcome.Success, AuditEntityTypes.Case, id, $"Case {id}", a.Details, a.CorrelationId, changes);
    }

    internal static AuditLogEntryDto FromTest(Domain.TestResults.TestResultAuditEntry a) =>
        new(TestPrefix + a.Id.ToString(CultureInfo.InvariantCulture), a.OccurredAtUtc, a.ActorId, a.Actor,
            AuditCategories.TestResults, a.Action, AuditOutcome.Success,
            a.RunId is null ? null : AuditEntityTypes.TestRun, a.RunId?.ToString(CultureInfo.InvariantCulture),
            a.RunId is { } run ? $"Run {run}" : null, a.Details, a.CorrelationId, []);

    private static string? StatusText(CaseStatus? status) => status switch
    {
        null => null,
        CaseStatus.InProgress => "In progress",
        { } s => s.ToString(),
    };

    private static Dictionary<string, string?> Describe(AuditQuery q)
    {
        var filters = new Dictionary<string, string?>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                filters[name] = value;
            }
        }
        Add("From", q.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("To", q.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("Category", q.Category);
        // Text typed into the filters is not stored (it could be anything); only that a text filter was used.
        Add("Actor", string.IsNullOrWhiteSpace(q.Actor) ? null : "(text filter)");
        Add("Entity type", q.EntityType);
        Add("Entity ID", q.EntityId);
        Add("Outcome", q.Outcome?.ToString());
        Add("Search", string.IsNullOrWhiteSpace(q.Search) ? null : "(text filter)");
        return filters;
    }

    [LoggerMessage(EventId = 3030, EventName = "AuditLogExported", Level = LogLevel.Information,
        Message = "{Rows} audit entries exported to CSV by {ActorId}")]
    private static partial void Exported(ILogger logger, int rows, string? actorId);
}
