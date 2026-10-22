using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.TestKit.Fakes;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Wraps the TestKit <see cref="FakeCaseNotificationService"/> so a test can make the feed, the unread count or
/// "mark all as read" fail or throw without changing the fake's rules.
/// </summary>
public sealed class ScriptedNotificationService(FakeCaseNotificationService inner) : ICaseNotificationService
{
    public Error? FeedError { get; set; }
    public Exception? FeedException { get; set; }
    public Error? MarkAllReadError { get; set; }
    public Exception? CountException { get; set; }
    public int CountCalls { get; private set; }

    /// <summary>When set, feed loads wait for it, so a test can look at the page while a load is running.</summary>
    public TaskCompletionSource? FeedGate { get; set; }

    public async Task<Result<CaseNotificationFeed>> GetFeedAsync(int max, CancellationToken ct)
    {
        if (FeedGate is { } gate)
        {
            await gate.Task;
        }
        if (FeedException is { } ex)
        {
            throw ex;
        }
        return FeedError is { } error ? Result<CaseNotificationFeed>.Failure(error) : await inner.GetFeedAsync(max, ct);
    }

    public Task<Result<int>> GetUnreadCountAsync(CancellationToken ct)
    {
        CountCalls++;
        if (CountException is { } ex)
        {
            throw ex;
        }
        return inner.GetUnreadCountAsync(ct);
    }

    public Task<Result<CaseNotificationFeed>> MarkAllReadAsync(CancellationToken ct) =>
        MarkAllReadError is { } error ? Task.FromResult(Result<CaseNotificationFeed>.Failure(error)) : inner.MarkAllReadAsync(ct);

    public Task<Result<CaseNotificationFeed>> MarkReadAsync(long notificationId, CancellationToken ct) =>
        inner.MarkReadAsync(notificationId, ct);
}

/// <summary>
/// Wraps the TestKit <see cref="InMemoryAttachmentService"/> so a test can make listing or uploading fail or throw.
/// </summary>
public sealed class ScriptedAttachmentService(InMemoryAttachmentService inner) : IAttachmentService
{
    public Exception? ListException { get; set; }
    public Error? NextUploadError { get; set; }
    public Exception? NextUploadException { get; set; }

    public Task<Result<AttachmentDto>> UploadAsync(long caseId, string fileName, string contentType, long declaredSize, Stream content, CancellationToken ct)
    {
        if (NextUploadException is { } ex)
        {
            NextUploadException = null;
            throw ex;
        }
        if (NextUploadError is { } error)
        {
            NextUploadError = null;
            return Task.FromResult(Result<AttachmentDto>.Failure(error));
        }
        return inner.UploadAsync(caseId, fileName, contentType, declaredSize, content, ct);
    }

    public Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(long caseId, CancellationToken ct)
    {
        if (ListException is { } ex)
        {
            throw ex;
        }
        return inner.ListAsync(caseId, ct);
    }

    public Task<Result<AttachmentContent>> OpenReadAsync(long caseId, Guid attachmentId, CancellationToken ct) => inner.OpenReadAsync(caseId, attachmentId, ct);
}
