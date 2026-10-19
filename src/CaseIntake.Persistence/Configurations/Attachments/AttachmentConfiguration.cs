using CaseIntake.Domain.Attachments;
using CaseIntake.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Attachments;

/// <summary>
/// Attachment metadata (E4-S4). The bytes live in the attachment store; this row records where (a server-generated
/// key) and what (size, SHA-256, allowlisted type). Rows are written once: every column rejects updates, and the
/// foreign key refuses deleting a case that has attachments (no delete operation exists, OQ-17).
/// </summary>
internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", t =>
        {
            t.HasCheckConstraint("CK_Attachment_SizeBytes_Positive", "\"SizeBytes\" > 0");
            t.HasCheckConstraint("CK_Attachment_Sha256_Hex",
                $"\"Sha256\" ~ '^[0-9a-f]{{{AttachmentFieldLimits.Sha256HexLength}}}$'");
            t.HasCheckConstraint("CK_Attachment_StorageKey_Hex",
                $"\"StorageKey\" ~ '^[0-9a-f]{{{AttachmentFieldLimits.StorageKeyLength}}}$'");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.FileName).HasMaxLength(AttachmentFieldLimits.FileNameMaxLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(AttachmentFieldLimits.ContentTypeMaxLength).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(AttachmentFieldLimits.Sha256HexLength).IsFixedLength().IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(AttachmentFieldLimits.StorageKeyLength).IsFixedLength().IsRequired();
        builder.Property(a => a.UploadedById).HasMaxLength(CaseFieldLimits.ActorMaxLength).IsRequired();
        builder.Property(a => a.UploadedBy).HasMaxLength(CaseFieldLimits.ActorMaxLength).IsRequired();
        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        builder.HasOne(a => a.Case).WithMany().HasForeignKey(a => a.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.CaseId, a.UploadedAtUtc, a.Id });
        builder.HasIndex(a => a.StorageKey).IsUnique();
    }
}
