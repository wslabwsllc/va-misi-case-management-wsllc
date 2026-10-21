namespace CaseIntake.Domain.Cases;

/// <summary>
/// One notification a user marked as read on its own (redesign). Together with <see cref="CaseNotificationCursor"/>:
/// an activity entry is unread when it is newer than the cursor and has no row here. "Mark all as read" moves the cursor
/// and removes the rows it covers.
/// </summary>
public sealed class CaseNotificationRead
{
    private CaseNotificationRead()
    {
    }

    /// <summary>Identity user ID.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The audit entry the notification shows.</summary>
    public long AuditEntryId { get; private set; }

    public DateTimeOffset ReadAtUtc { get; private set; }

    public static CaseNotificationRead Create(string userId, long auditEntryId, DateTimeOffset readAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(auditEntryId);
        return new CaseNotificationRead { UserId = CaseTextRules.TruncateActor(userId), AuditEntryId = auditEntryId, ReadAtUtc = readAtUtc.ToUniversalTime() };
    }
}
