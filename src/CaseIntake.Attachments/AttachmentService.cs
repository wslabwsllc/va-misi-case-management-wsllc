using System.Globalization;
using System.Runtime.ExceptionServices;
using CaseIntake.Attachments.Storage;
using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Attachments;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.Attachments;

/// <summary>
/// Attachment upload, list and download (E4-S5/S6). Each operation reads the actor once, checks its permission and
/// uses its own short-lived context, like the case service.
/// <para>
/// Upload order: authorize and validate the request, check the case, stream into a temporary file while counting
/// and hashing, check the leading bytes against the claimed type, move it to its final server-generated key, then commit metadata plus the audit entry in one
/// transaction that re-checks, under a share lock, that the case is still open. A database and a filesystem cannot share a transaction, so a failed commit is compensated: the
/// attachment ID is looked up with a fresh context and the file is deleted only when the row is confirmed absent.
/// If that cannot be confirmed the file is kept for the startup reconciler and the caller is told the outcome is
/// unknown (ADR 0009 describes the remaining crash window).
/// </para>
/// </summary>
internal sealed class AttachmentService(
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    IAttachmentStore store,
    IOptions<AttachmentOptions> options,
    TimeProvider timeProvider,
    ILogger<AttachmentService> logger) : IAttachmentService
{
    private static readonly TimeSpan CommitCheckTimeout = TimeSpan.FromSeconds(10);

    public async Task<Result<AttachmentDto>> UploadAsync(long caseId, string fileName, string contentType,
        long declaredSize, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.AttachmentsUpload))
        {
            return Denied<AttachmentDto>(nameof(UploadAsync), actor, Permissions.AttachmentsUpload, caseId);
        }

        var settings = options.Value;
        var displayName = AttachmentFileName.Sanitize(fileName);
        var storedType = settings.MatchAllowedType(contentType, AttachmentFileName.Extension(displayName));
        if (storedType is null)
        {
            return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.TypeNotAllowed(settings));
        }
        if (declaredSize <= 0)
        {
            return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.Empty);
        }
        if (declaredSize > settings.MaxBytes)
        {
            return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.TooLarge(settings));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var caseEntity = await db.Cases.SingleOrDefaultAsync(c => c.Id == caseId, ct);
        if (caseEntity is null)
        {
            return Result<AttachmentDto>.Failure(AttachmentErrors.CaseNotFound);
        }
        if (caseEntity.IsClosed)
        {
            return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.CaseClosed);
        }

        // declaredSize is already within MaxBytes, so it is the read limit: more bytes than declared stop the read.
        StagedFile staged;
        try
        {
            staged = await store.StageAsync(content, declaredSize, ct);
        }
        catch (UploadSourceException)
        {
            return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.Incomplete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AttachmentLog.StorageFailed(logger, caseId, ex.GetType().Name);   // the server's fault, not the client's
            throw;
        }
        if (staged.ExceededLimit || staged.SizeBytes != declaredSize)
        {
            store.DeleteStaged(staged);
            return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.SizeMismatch);
        }
        if (!FileSignatures.Matches(storedType, staged.Header))
        {
            store.DeleteStaged(staged);
            return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.ContentMismatch);
        }

        var now = timeProvider.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var key = id.ToString("N", CultureInfo.InvariantCulture);
        Attachment? attachment = null;
        var promoted = false;
        try
        {
            store.Promote(staged, key);
            promoted = true;
            attachment = Attachment.Record(id, caseEntity, displayName, storedType, staged.SizeBytes, staged.Sha256, key, now,
                actor.UserId!, AuditAppender.ActorName(actor));
            db.Attachments.Add(attachment);
            AuditAppender.Append(db, caseEntity, actor, AuditEvent.AttachmentUploaded(now, id, staged.SizeBytes, storedType));
            if (!await CommitWhileCaseOpenAsync(db, caseId))
            {
                // The execution strategy may have retried after a COMMIT that did reach the database; only content
                // confirmed as uncommitted is deleted, otherwise the confirmed upload is reported as such.
                switch (await IsCommittedAsync(id))
                {
                    case true:
                        AttachmentLog.Uploaded(logger, id, staged.SizeBytes, caseId, actor.UserId!);
                        return Result<AttachmentDto>.Success(ToDto(attachment));
                    case false:
                        DeleteContent(id, key);
                        return Rejected<AttachmentDto>(nameof(UploadAsync), caseId, AttachmentErrors.CaseClosed);
                    default:
                        AttachmentLog.OutcomeUncertain(logger, id, caseId, nameof(AttachmentErrors.CaseClosed));
                        return Result<AttachmentDto>.Failure(AttachmentErrors.OutcomeUnknown);
                }
            }
        }
        catch (Exception ex)
        {
            return await RecoverFailedCommitAsync(ex, caseId, id, key, staged, promoted, attachment);
        }

        AttachmentLog.Uploaded(logger, id, staged.SizeBytes, caseId, actor.UserId!);
        return Result<AttachmentDto>.Success(ToDto(attachment));
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(long caseId, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView))
        {
            return Denied<IReadOnlyList<AttachmentDto>>(nameof(ListAsync), actor, Permissions.CasesView, caseId);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var items = await db.Attachments.AsNoTracking()
            .Where(a => a.CaseId == caseId)
            .OrderByDescending(a => a.UploadedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => new AttachmentDto(a.Id, a.CaseId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAtUtc, a.UploadedBy))
            .ToListAsync(ct);
        // Rows prove the case exists (foreign key); only an empty list needs the existence check.
        if (items.Count == 0 && !await db.Cases.AnyAsync(c => c.Id == caseId, ct))
        {
            return Result<IReadOnlyList<AttachmentDto>>.Failure(AttachmentErrors.CaseNotFound);
        }
        return Result<IReadOnlyList<AttachmentDto>>.Success(items);
    }

    /// <summary>
    /// Opens an attachment's content. The attachment must belong to <paramref name="caseId"/>: an ID from another case
    /// is reported exactly like an unknown ID. The context is disposed before returning; the caller owns the stream.
    /// </summary>
    public async Task<Result<AttachmentContent>> OpenReadAsync(long caseId, Guid attachmentId, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.AttachmentsDownload))
        {
            return Denied<AttachmentContent>(nameof(OpenReadAsync), actor, Permissions.AttachmentsDownload, caseId);
        }

        Attachment? attachment;
        await using (var db = await contextFactory.CreateDbContextAsync(ct))
        {
            attachment = await db.Attachments.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == attachmentId && a.CaseId == caseId, ct);
        }
        if (attachment is null)
        {
            return Result<AttachmentContent>.Failure(AttachmentErrors.AttachmentNotFound);
        }

        var stream = store.OpenRead(attachment.StorageKey);
        if (stream is null)
        {
            AttachmentLog.ContentMissing(logger, attachment.Id, caseId);
            return Result<AttachmentContent>.Failure(AttachmentErrors.AttachmentNotFound);
        }
        // A cheap integrity check: truncated or replaced content is never served as if intact. (The SHA-256 is not
        // re-hashed on every download; that would read the whole file twice.)
        if (stream.Length != attachment.SizeBytes)
        {
            var actual = stream.Length;
            await stream.DisposeAsync();
            AttachmentLog.ContentCorrupt(logger, attachment.Id, caseId, actual, attachment.SizeBytes);
            return Result<AttachmentContent>.Failure(AttachmentErrors.AttachmentNotFound);
        }

        return Result<AttachmentContent>.Success(new AttachmentContent(stream, attachment.FileName, attachment.ContentType, attachment.SizeBytes));
    }

    /// <summary>
    /// Compensates an upload whose promotion or commit threw. Returns success when the commit is confirmed after all,
    /// <see cref="AttachmentErrors.OutcomeUnknown"/> when it cannot be determined, and otherwise removes the content
    /// and rethrows the original failure (including caller cancellation) for the UI boundary to handle.
    /// </summary>
    private async Task<Result<AttachmentDto>> RecoverFailedCommitAsync(Exception failure, long caseId, Guid id, string key,
        StagedFile staged, bool promoted, Attachment? attachment)
    {
        var failureType = failure.GetType().Name;
        if (!promoted)
        {
            store.DeleteStaged(staged);
            AttachmentLog.UploadFailed(logger, id, caseId, failureType);
            ExceptionDispatchInfo.Throw(failure);
        }

        switch (await IsCommittedAsync(id))
        {
            case true when attachment is not null:
                AttachmentLog.CommitRecovered(logger, id, caseId, failureType);
                AttachmentLog.Uploaded(logger, id, attachment.SizeBytes, caseId, attachment.UploadedById);
                return Result<AttachmentDto>.Success(ToDto(attachment));
            case true:   // committed but nothing to report it with: never delete committed content
            case null:
                AttachmentLog.OutcomeUncertain(logger, id, caseId, failureType);
                if (failure is OperationCanceledException)
                {
                    ExceptionDispatchInfo.Throw(failure);
                }
                return Result<AttachmentDto>.Failure(AttachmentErrors.OutcomeUnknown);
        }

        DeleteContent(id, key);
        AttachmentLog.UploadFailed(logger, id, caseId, failureType);
        ExceptionDispatchInfo.Throw(failure);
        return default!;   // unreachable
    }

    /// <summary>
    /// Commits the metadata and audit entry in one transaction that first takes a share lock on the case row and
    /// re-reads its status: a case closed while the file was streaming is refused, and a concurrent status change
    /// waits for this commit (the earlier check alone would let a just-closed case gain an attachment). Runs inside
    /// the execution strategy, as explicit transactions must with retries enabled. The caller's cancellation is not
    /// applied from here on: abandoning a COMMIT half-way would leave its outcome unknown.
    /// </summary>
    private static async Task<bool> CommitWhileCaseOpenAsync(AppDbContext db, long caseId)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var committed = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var status = (await db.Database
                .SqlQuery<int>($"SELECT \"Status\" AS \"Value\" FROM \"Cases\" WHERE \"Id\" = {caseId} FOR SHARE")
                .ToListAsync(CancellationToken.None)).Single();
            if (status == (int)CaseStatus.Closed)
            {
                return false;   // disposing the transaction rolls it back
            }

            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            return true;
        });
        if (committed)
        {
            db.ChangeTracker.AcceptAllChanges();
        }
        return committed;
    }

    /// <summary>Removes content known not to be committed; a failure is logged and left for the startup reconciler.</summary>
    private void DeleteContent(Guid id, string key)
    {
        try
        {
            store.Delete(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AttachmentLog.CleanupFailed(logger, id, ex.GetType().Name);
        }
    }

    /// <summary>Whether the attachment row exists, checked with a fresh context; null when the check itself fails.</summary>
    private async Task<bool?> IsCommittedAsync(Guid id)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CommitCheckTimeout);
            await using var db = await contextFactory.CreateDbContextAsync(timeout.Token);
            return await db.Attachments.AsNoTracking().AnyAsync(a => a.Id == id, timeout.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static AttachmentDto ToDto(Attachment a) =>
        new(a.Id, a.CaseId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAtUtc, a.UploadedBy);

    private Result<T> Denied<T>(string operation, OperationActor actor, string permission, long caseId)
    {
        AttachmentLog.AuthorizationDenied(logger, actor.UserId, permission, caseId.ToString(CultureInfo.InvariantCulture), operation);
        return Result<T>.Failure(AttachmentErrors.Forbidden);
    }

    private Result<T> Rejected<T>(string operation, long caseId, Error error)
    {
        AttachmentLog.Rejected(logger, operation, caseId, error.Code);
        return Result<T>.Failure(error);
    }
}
