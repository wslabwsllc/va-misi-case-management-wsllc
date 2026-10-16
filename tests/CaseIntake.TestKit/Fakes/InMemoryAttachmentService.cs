using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;

namespace CaseIntake.TestKit.Fakes;

/// <summary>In-memory <see cref="IAttachmentService"/> for isolated UI and unit tests. Not a runtime implementation.</summary>
public sealed class InMemoryAttachmentService(ICurrentUser currentUser, TimeProvider time) : IAttachmentService
{
    private readonly List<(AttachmentDto Dto, byte[] Bytes)> _items = [];

    public async Task<Result<AttachmentDto>> UploadAsync(long caseId, string fileName, string contentType,
        long declaredSize, Stream content, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.AttachmentsUpload))
        {
            return Result<AttachmentDto>.Failure(new Error(ErrorKind.Forbidden, "Forbidden", "Not allowed."));
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var dto = new AttachmentDto(Guid.NewGuid(), caseId, fileName, contentType, buffer.Length, time.GetUtcNow(), actor.UserId!);
        _items.Add((dto, buffer.ToArray()));
        return Result<AttachmentDto>.Success(dto);
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(long caseId, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView))
        {
            return Result<IReadOnlyList<AttachmentDto>>.Failure(new Error(ErrorKind.Forbidden, "Forbidden", "Not allowed."));
        }

        return Result<IReadOnlyList<AttachmentDto>>.Success([.. _items.Where(i => i.Dto.CaseId == caseId).Select(i => i.Dto)]);
    }

    public async Task<Result<AttachmentContent>> OpenReadAsync(long caseId, Guid attachmentId, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.AttachmentsDownload))
        {
            return Result<AttachmentContent>.Failure(new Error(ErrorKind.Forbidden, "Forbidden", "Not allowed."));
        }

        var item = _items.FirstOrDefault(i => i.Dto.CaseId == caseId && i.Dto.Id == attachmentId);
        return item.Dto is null
            ? Result<AttachmentContent>.Failure(new Error(ErrorKind.NotFound, "AttachmentNotFound", "Attachment not found."))
            : Result<AttachmentContent>.Success(new AttachmentContent(
                new MemoryStream(item.Bytes), item.Dto.FileName, item.Dto.ContentType, item.Dto.SizeBytes));
    }
}
