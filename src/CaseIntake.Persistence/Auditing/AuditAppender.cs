using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Persistence.Auditing;

/// <summary>
/// Shared audit helper for case mutations (E2) and attachment uploads (E4). It adds one entry to the caller's
/// context and returns without saving, so the entry commits in the same SaveChanges/transaction as the business
/// change, or not at all. It never opens a context or transaction.
/// </summary>
public static class AuditAppender
{
    /// <exception cref="InvalidOperationException">The actor is not authenticated.</exception>
    public static CaseAuditEntry Append(AppDbContext db, Case caseEntity, OperationActor actor, AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsAuthenticated)
        {
            throw new InvalidOperationException("Audit entries require an authenticated actor.");
        }

        var entry = CaseAuditEntry.Record(caseEntity, actor.UserId!, ActorName(actor), auditEvent);
        db.CaseAuditEntries.Add(entry);
        return entry;
    }

    /// <summary>The name recorded in audit fields: the display (user) name, falling back to the user ID.</summary>
    public static string ActorName(OperationActor actor) =>
        string.IsNullOrWhiteSpace(actor.DisplayName) ? actor.UserId ?? string.Empty : actor.DisplayName;
}
