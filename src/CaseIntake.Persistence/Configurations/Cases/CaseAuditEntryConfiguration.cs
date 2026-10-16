using CaseIntake.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Cases;

/// <summary>Append-only audit trail (E2-S4). Deleting a case that has audit rows is refused by the foreign key.</summary>
internal sealed class CaseAuditEntryConfiguration : IEntityTypeConfiguration<CaseAuditEntry>
{
    public void Configure(EntityTypeBuilder<CaseAuditEntry> builder)
    {
        builder.ToTable("CaseAuditEntries", t =>
        {
            t.HasCheckConstraint("CK_CaseAuditEntry_FromStatus_Range", "\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 0 AND 4");
            t.HasCheckConstraint("CK_CaseAuditEntry_ToStatus_Range", "\"ToStatus\" IS NULL OR \"ToStatus\" BETWEEN 0 AND 4");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn().ValueGeneratedOnAdd();

        builder.Property(a => a.ActorId).HasMaxLength(CaseFieldLimits.ActorMaxLength).IsRequired();
        builder.Property(a => a.Actor).HasMaxLength(CaseFieldLimits.ActorMaxLength).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(CaseFieldLimits.AuditActionMaxLength).IsRequired();
        builder.Property(a => a.Details).HasMaxLength(CaseFieldLimits.AuditDetailsMaxLength);
        builder.Property(a => a.FromStatus).HasConversion<int?>();
        builder.Property(a => a.ToStatus).HasConversion<int?>();
        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        builder.HasOne(a => a.Case).WithMany().HasForeignKey(a => a.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.CaseId, a.OccurredAtUtc, a.Id });
    }
}
