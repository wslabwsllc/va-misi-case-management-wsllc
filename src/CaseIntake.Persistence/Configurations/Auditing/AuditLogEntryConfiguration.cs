using CaseIntake.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Auditing;

/// <summary>
/// The append-only system audit log (ADR 0014). No foreign keys: an entry outlives the user or role it is about.
/// Every column rejects updates; the SaveChanges guard also rejects deletes.
/// </summary>
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries", t =>
            t.HasCheckConstraint("CK_AuditLogEntry_Outcome", "\"Outcome\" IN ('Success', 'Failure', 'Denied')"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn().ValueGeneratedOnAdd();
        builder.Property(a => a.ActorId).HasMaxLength(AuditLogEntry.ShortTextMaxLength);
        builder.Property(a => a.ActorName).HasMaxLength(AuditLogEntry.ShortTextMaxLength);
        builder.Property(a => a.Category).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.EntityType).HasMaxLength(64);
        builder.Property(a => a.EntityId).HasMaxLength(AuditLogEntry.ShortTextMaxLength);
        builder.Property(a => a.EntityName).HasMaxLength(AuditLogEntry.ShortTextMaxLength);
        builder.Property(a => a.Summary).HasMaxLength(AuditLogEntry.SummaryMaxLength);
        builder.Property(a => a.Before).HasColumnType("jsonb");
        builder.Property(a => a.After).HasColumnType("jsonb");
        builder.Property(a => a.CorrelationId).HasMaxLength(AuditLogEntry.CorrelationIdMaxLength).IsRequired();
        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAtUtc });
        builder.HasIndex(a => a.ActorId);
        builder.HasIndex(a => a.CorrelationId);
    }
}
