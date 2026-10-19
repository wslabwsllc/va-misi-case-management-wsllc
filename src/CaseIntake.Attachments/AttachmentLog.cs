using CaseIntake.Contracts.Security;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Attachments;

/// <summary>
/// Attachment log events (EventIds 4100-4119 of the E4 range, plus the shared AuthorizationDenied event). Only IDs,
/// sizes, error codes, counts and exception type names are logged; never file names, file content, storage paths
/// or exception messages (decision D8).
/// </summary>
internal static partial class AttachmentLog
{
    [LoggerMessage(EventId = 4101, EventName = "AttachmentUploaded", Level = LogLevel.Information,
        Message = "Attachment {AttachmentId} ({SizeBytes} bytes) added to case {CaseId} by {ActorId}")]
    public static partial void Uploaded(ILogger logger, Guid attachmentId, long sizeBytes, long caseId, string actorId);

    [LoggerMessage(EventId = 4102, EventName = "AttachmentRequestRejected", Level = LogLevel.Information,
        Message = "Attachment operation {Operation} on case {CaseId} rejected with {ErrorCode}")]
    public static partial void Rejected(ILogger logger, string operation, long caseId, string errorCode);

    [LoggerMessage(EventId = 4104, EventName = "AttachmentCleanupFailed", Level = LogLevel.Warning,
        Message = "Content of failed upload {AttachmentId} could not be removed ({ExceptionType}); the startup reconciler will retry")]
    public static partial void CleanupFailed(ILogger logger, Guid attachmentId, string exceptionType);

    [LoggerMessage(EventId = 4105, EventName = "AttachmentContentMissing", Level = LogLevel.Error,
        Message = "Stored content for attachment {AttachmentId} on case {CaseId} is missing")]
    public static partial void ContentMissing(ILogger logger, Guid attachmentId, long caseId);

    [LoggerMessage(EventId = 4112, EventName = "AttachmentContentCorrupt", Level = LogLevel.Error,
        Message = "Stored content for attachment {AttachmentId} on case {CaseId} has {ActualBytes} bytes instead of {ExpectedBytes}")]
    public static partial void ContentCorrupt(ILogger logger, Guid attachmentId, long caseId, long actualBytes, long expectedBytes);

    [LoggerMessage(EventId = 4106, EventName = "AttachmentUploadOutcomeUncertain", Level = LogLevel.Error,
        Message = "Upload {AttachmentId} to case {CaseId} failed ({ExceptionType}) and its commit could not be confirmed; content kept for reconciliation")]
    public static partial void OutcomeUncertain(ILogger logger, Guid attachmentId, long caseId, string exceptionType);

    [LoggerMessage(EventId = 4107, EventName = "AttachmentUploadFailed", Level = LogLevel.Warning,
        Message = "Upload {AttachmentId} to case {CaseId} failed ({ExceptionType}); nothing was committed")]
    public static partial void UploadFailed(ILogger logger, Guid attachmentId, long caseId, string exceptionType);

    [LoggerMessage(EventId = 4108, EventName = "AttachmentCommitRecovered", Level = LogLevel.Warning,
        Message = "Upload {AttachmentId} to case {CaseId} reported {ExceptionType} but its commit was confirmed")]
    public static partial void CommitRecovered(ILogger logger, Guid attachmentId, long caseId, string exceptionType);

    [LoggerMessage(EventId = 4109, EventName = "AttachmentStorageFailed", Level = LogLevel.Error,
        Message = "Attachment storage failed while receiving an upload to case {CaseId} ({ExceptionType})")]
    public static partial void StorageFailed(ILogger logger, long caseId, string exceptionType);

    [LoggerMessage(EventId = 4110, EventName = "AttachmentStorageReconciled", Level = LogLevel.Information,
        Message = "Attachment storage reconciled: {TempDeleted} temporary and {OrphansDeleted} unreferenced files removed, {MissingContent} attachments without content, {Failures} failures")]
    public static partial void Reconciled(ILogger logger, int tempDeleted, int orphansDeleted, int missingContent, int failures);

    [LoggerMessage(EventId = 4111, EventName = "AttachmentStorageReconcileFailed", Level = LogLevel.Warning,
        Message = "Attachment storage reconciliation failed ({ExceptionType}); it runs again on the next start")]
    public static partial void ReconcileFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = SecurityEvents.AuthorizationDeniedId, EventName = SecurityEvents.AuthorizationDeniedName,
        Level = LogLevel.Warning, Message = "Authorization denied for {UserId}: {Permission} on {ResourceId} ({Operation})")]
    public static partial void AuthorizationDenied(ILogger logger, string? userId, string permission, string? resourceId, string operation);
}
