using CaseIntake.Contracts.Common;

namespace CaseIntake.Contracts.Attachments;

public sealed record AttachmentDto(Guid Id, long CaseId, string FileName, string ContentType,
    long SizeBytes, DateTimeOffset UploadedAtUtc, string UploadedBy);
public sealed record AttachmentContent(Stream Content, string FileName, string ContentType, long SizeBytes);

public interface IAttachmentService
{
    Task<Result<AttachmentDto>> UploadAsync(long caseId, string fileName, string contentType,
        long declaredSize, Stream content, CancellationToken ct);
    Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(long caseId, CancellationToken ct);
    Task<Result<AttachmentContent>> OpenReadAsync(long caseId, Guid attachmentId, CancellationToken ct);
}

/// <summary>Download URL used by the UI (E5) and implemented by E4.</summary>
public static class AttachmentRoutes
{
    public static string Content(long caseId, Guid id) => $"/cases/{caseId}/attachments/{id}/content";
}
