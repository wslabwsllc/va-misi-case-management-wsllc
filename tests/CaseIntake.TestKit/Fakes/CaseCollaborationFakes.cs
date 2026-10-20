using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;

namespace CaseIntake.TestKit.Fakes;

/// <summary>Fixed assignee list (the two demo users who work cases) for component tests.</summary>
public sealed class InMemoryAssigneeDirectory : ICaseAssigneeDirectory
{
    public List<AssigneeDto> Assignees { get; } =
    [
        new("caseworker@example.test", AppRoles.CaseWorker),
        new("supervisor@example.test", AppRoles.Supervisor),
    ];

    public Task<Result<IReadOnlyList<AssigneeDto>>> ListAsync(CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<AssigneeDto>>.Success([.. Assignees]));
}

/// <summary>Scriptable notification feed for component tests: set <see cref="Items"/>; Mark all as read clears the unread flags.</summary>
public sealed class FakeCaseNotificationService(TimeProvider time) : ICaseNotificationService
{
    public List<CaseNotificationDto> Items { get; } = [];

    public DateTimeOffset? LastReadAtUtc { get; private set; }

    public int MarkAllReadCalls { get; private set; }

    public Task<Result<CaseNotificationFeed>> GetFeedAsync(int max, CancellationToken ct) =>
        Task.FromResult(Result<CaseNotificationFeed>.Success(Feed(max)));

    public Task<Result<int>> GetUnreadCountAsync(CancellationToken ct) =>
        Task.FromResult(Result<int>.Success(Items.Count(i => i.Unread)));

    public Task<Result<CaseNotificationFeed>> MarkAllReadAsync(CancellationToken ct)
    {
        MarkAllReadCalls++;
        LastReadAtUtc = time.GetUtcNow();
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i] = Items[i] with { Unread = false };
        }
        return Task.FromResult(Result<CaseNotificationFeed>.Success(Feed(50)));
    }

    private CaseNotificationFeed Feed(int max) =>
        new([.. Items.OrderByDescending(i => i.OccurredAtUtc).Take(max)], Items.Count(i => i.Unread), LastReadAtUtc);
}
