using CaseIntake.Contracts.Auditing;
using CaseIntake.Persistence.Auditing;

namespace CaseIntake.Identity.Account;

/// <summary>
/// Authentication events in the system audit log (ADR 0014). The user name is recorded only for an account that
/// exists: what someone typed into the sign-in form for an unknown account could be anything, even a password.
/// </summary>
internal static class AuthenticationAudit
{
    public static Task WriteAsync(AuditLogWriter writer, string action, AuditOutcome outcome, string? userId, string? userName, string summary) =>
        writer.WriteAsync(new AuditRecord(AuditCategories.Authentication, action, outcome, userId, userName,
            userId is null ? null : AuditEntityTypes.User, userId, userName, summary), CancellationToken.None);
}
