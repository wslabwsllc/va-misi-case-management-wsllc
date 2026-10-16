using CaseIntake.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CaseIntake.Persistence.Auditing;

/// <summary>Raised when application code tries to change or delete a saved audit entry.</summary>
public sealed class AuditTrailModificationException()
    : InvalidOperationException("The case audit trail is append-only; entries cannot be modified or deleted.");

/// <summary>SaveChanges guard (E2-S4): audit entries may only be added. Runs before every save of <see cref="AppDbContext"/>.</summary>
internal static class AuditTrailGuard
{
    public static void Validate(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries<CaseAuditEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new AuditTrailModificationException();
            }
        }
    }
}
