using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Persistence.Auditing;

/// <summary>
/// Adds a system audit log entry (ADR 0014) to the caller's context without saving, so it commits in the same
/// transaction as the change it describes, or not at all. The correlation ID is the ambient operation ID.
/// </summary>
public static class SystemAudit
{
    public static AuditLogEntry Append(AppDbContext db, AuditRecord record, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(db);
        var entry = AuditLogEntry.From(record, occurredAtUtc, OperationContext.CurrentOrNew());
        db.AuditLogEntries.Add(entry);
        return entry;
    }
}

/// <summary>
/// Writes a system audit entry on its own short-lived context, for events that have no business change to commit with
/// (sign-in, sign-out, an access denial). A write that fails is logged as <c>AuditWriteFailed</c> (1020) and does not
/// fail the caller: a database outage must not lock everyone out. Singleton.
/// </summary>
public sealed partial class AuditLogWriter(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider time,
    ILogger<AuditLogWriter> logger)
{
    /// <returns>True when the entry was stored.</returns>
    public async Task<bool> WriteAsync(AuditRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            SystemAudit.Append(db, record, time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuditWriteFailed(logger, record.Category, record.Action, ex.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(EventId = 1020, EventName = "AuditWriteFailed", Level = LogLevel.Error,
        Message = "Audit entry {Category}/{Action} could not be stored ({ExceptionType})")]
    private static partial void AuditWriteFailed(ILogger logger, string category, string action, string exceptionType);
}
