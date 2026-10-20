namespace CaseIntake.Domain.Cases;

/// <summary>
/// How far a user has read the case activity feed (issue #21): activity after <see cref="LastReadAtUtc"/> by someone else
/// is unread. One row per user, created on the first "mark as read".
/// </summary>
public sealed class CaseNotificationCursor
{
    private CaseNotificationCursor()
    {
    }

    /// <summary>Identity user ID.</summary>
    public string UserId { get; private set; } = string.Empty;

    public DateTimeOffset LastReadAtUtc { get; private set; }

    public static CaseNotificationCursor Create(string userId, DateTimeOffset readAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new CaseNotificationCursor { UserId = CaseTextRules.TruncateActor(userId), LastReadAtUtc = readAtUtc.ToUniversalTime() };
    }

    /// <summary>Moves the cursor forward only; an older time (clock skew between requests) is ignored.</summary>
    public void MarkReadAt(DateTimeOffset readAtUtc)
    {
        var utc = readAtUtc.ToUniversalTime();
        if (utc > LastReadAtUtc)
        {
            LastReadAtUtc = utc;
        }
    }
}
