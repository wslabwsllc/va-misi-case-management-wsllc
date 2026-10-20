using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.Cases;

/// <summary>Notification settings (<c>Notifications</c> configuration section). Defaults need no configuration.</summary>
public sealed class CaseNotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>How far back the activity feed reaches.</summary>
    public int WindowDays { get; set; } = 14;
}

/// <summary>
/// The case activity feed (issue #21). Every audit entry written by someone else in the last
/// <see cref="CaseNotificationOptions.WindowDays"/> days on a case the user can see is a notification; entries after the
/// user's <see cref="CaseNotificationCursor"/> are unread. Reads the append-only audit trail, so nothing extra is written
/// when cases change, and the feed always agrees with the case history.
/// </summary>
public sealed class CaseNotificationService(
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<CaseNotificationOptions> options,
    ILogger<CaseNotificationService> logger) : ICaseNotificationService
{
    public const int MaxItems = 100;

    public async Task<Result<CaseNotificationFeed>> GetFeedAsync(int max, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (Denied(actor, nameof(GetFeedAsync)) is { } denied)
        {
            return Result<CaseNotificationFeed>.Failure(denied);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return Result<CaseNotificationFeed>.Success(await ReadFeedAsync(db, actor, Math.Clamp(max, 1, MaxItems), ct));
    }

    public async Task<Result<int>> GetUnreadCountAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (Denied(actor, nameof(GetUnreadCountAsync)) is { } denied)
        {
            return Result<int>.Failure(denied);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var lastRead = await LastReadAsync(db, actor.UserId!, ct);
        var count = await Unread(Activity(db, actor), lastRead).CountAsync(ct);
        return Result<int>.Success(count);
    }

    public async Task<Result<CaseNotificationFeed>> MarkAllReadAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (Denied(actor, nameof(MarkAllReadAsync)) is { } denied)
        {
            return Result<CaseNotificationFeed>.Failure(denied);
        }

        var now = timeProvider.GetUtcNow();
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var cursor = await db.CaseNotificationCursors.SingleOrDefaultAsync(c => c.UserId == actor.UserId, ct);
        if (cursor is null)
        {
            db.CaseNotificationCursors.Add(CaseNotificationCursor.Create(actor.UserId!, now));
        }
        else
        {
            cursor.MarkReadAt(now);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (cursor is null)
        {
            // Another session of the same user created the cursor first: move that one forward instead.
            db.ChangeTracker.Clear();
            var existing = await db.CaseNotificationCursors.SingleAsync(c => c.UserId == actor.UserId, ct);
            existing.MarkReadAt(now);
            await db.SaveChangesAsync(ct);
        }

        return Result<CaseNotificationFeed>.Success(await ReadFeedAsync(db, actor, 50, ct));
    }

    private async Task<CaseNotificationFeed> ReadFeedAsync(AppDbContext db, OperationActor actor, int max, CancellationToken ct)
    {
        var lastRead = await LastReadAsync(db, actor.UserId!, ct);
        var me = AuditAppender.ActorName(actor);
        var activity = Activity(db, actor);
        var unread = await Unread(activity, lastRead).CountAsync(ct);
        var rows = await (
            from entry in activity
            join c in db.Cases on entry.CaseId equals c.Id
            orderby entry.OccurredAtUtc descending, entry.Id descending
            select new { entry.Id, entry.CaseId, c.Title, entry.Action, entry.FromStatus, entry.ToStatus, entry.Actor, entry.OccurredAtUtc, entry.Details, c.AssignedTo })
            .Take(max)
            .ToListAsync(ct);

        var items = rows.Select(r => new CaseNotificationDto(r.Id, r.CaseId, r.Title, r.Action, r.FromStatus, r.ToStatus,
                r.Actor, r.OccurredAtUtc, r.Details, Unread: lastRead is null || r.OccurredAtUtc > lastRead,
                AssignedToMe: string.Equals(r.AssignedTo, me, StringComparison.Ordinal),
                AssignedToYou: IsAssignmentTo(r.Action, r.Details, me)))
            .ToList();
        return new CaseNotificationFeed(items, unread, lastRead);
    }

    /// <summary>An Assigned entry whose new owner is <paramref name="me"/> (details "Assigned to me" or "Assigned to me (was …)").</summary>
    internal static bool IsAssignmentTo(string action, string? details, string me) =>
        action == AuditActions.Assigned && details is not null
        && (details == $"Assigned to {me}" || details.StartsWith($"Assigned to {me} (", StringComparison.Ordinal));

    /// <summary>Audit entries in the window that someone other than the actor wrote.</summary>
    private IQueryable<CaseAuditEntry> Activity(AppDbContext db, OperationActor actor)
    {
        var since = timeProvider.GetUtcNow().AddDays(-Math.Max(options.Value.WindowDays, 1));
        var userId = actor.UserId!;
        return db.CaseAuditEntries.AsNoTracking().Where(a => a.OccurredAtUtc >= since && a.ActorId != userId);
    }

    private static IQueryable<CaseAuditEntry> Unread(IQueryable<CaseAuditEntry> activity, DateTimeOffset? lastRead) =>
        lastRead is { } read ? activity.Where(a => a.OccurredAtUtc > read) : activity;

    private static async Task<DateTimeOffset?> LastReadAsync(AppDbContext db, string userId, CancellationToken ct) =>
        await db.CaseNotificationCursors.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => (DateTimeOffset?)c.LastReadAtUtc)
            .SingleOrDefaultAsync(ct);

    private Error? Denied(OperationActor actor, string operation)
    {
        if (actor.HasPermission(Permissions.CasesView))
        {
            return null;
        }
        CaseLog.AuthorizationDenied(logger, actor.UserId, Permissions.CasesView, null, operation);
        return CaseErrors.Forbidden;
    }
}
