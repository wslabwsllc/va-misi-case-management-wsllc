using CaseIntake.Domain.Attachments;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Attachments.Tests;

/// <summary>E4-S4: the domain's last line of defence rejects metadata the service should never produce.</summary>
public class AttachmentRecordTests
{
    private static readonly Case Owner = Case.Create("A valid case title", null, null, "tester", new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private static readonly string Hash = new('a', AttachmentFieldLimits.Sha256HexLength);
    private static readonly string Key = Guid.NewGuid().ToString("N");

    private static Attachment Record(Guid? id = null, string fileName = "a.pdf", string contentType = "application/pdf",
        long size = 1, string? sha = null, string? key = null) =>
        Attachment.Record(id ?? Guid.NewGuid(), Owner, fileName, contentType, size, sha ?? Hash, key ?? Key,
            DateTimeOffset.UnixEpoch, "user-id", "user");

    [Test]
    public void Valid_metadata_is_recorded()
    {
        var attachment = Record();

        Assert.Equal("a.pdf", attachment.FileName);
        Assert.Equal(TimeSpan.Zero, attachment.UploadedAtUtc.Offset);
    }

    public static TheoryData<Func<Attachment>> InvalidMetadata() =>
    [
        () => Record(id: Guid.Empty),
        () => Record(fileName: new string('n', AttachmentFieldLimits.FileNameMaxLength + 1)),
        () => Record(fileName: "bad\0name"),
        () => Record(contentType: "application/" + new string('x', AttachmentFieldLimits.ContentTypeMaxLength)),
        () => Record(size: 0),
        () => Record(size: -1),
        () => Record(sha: "ABC"),
        () => Record(sha: new string('A', AttachmentFieldLimits.Sha256HexLength)),
        () => Record(key: "../../etc/passwd"),
        () => Record(key: Key.ToUpperInvariant()),
    ];

    [TestCaseSource(nameof(InvalidMetadata))]
    public void Invalid_metadata_is_refused(Func<Attachment> record)
    {
        Assert.ThrowsAny<ArgumentException>(record);
    }
}
