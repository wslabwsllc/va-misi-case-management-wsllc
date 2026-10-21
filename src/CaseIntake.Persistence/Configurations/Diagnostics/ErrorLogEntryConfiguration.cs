using CaseIntake.Persistence.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Diagnostics;

/// <summary>The stored error log (ADR 0015). Captured columns reject updates; only the status columns change.</summary>
internal sealed class ErrorLogEntryConfiguration : IEntityTypeConfiguration<ErrorLogEntry>
{
    public void Configure(EntityTypeBuilder<ErrorLogEntry> builder)
    {
        builder.ToTable("ErrorLogEntries", t =>
        {
            t.HasCheckConstraint("CK_ErrorLogEntry_Level", "\"Level\" IN ('Error', 'Critical')");
            t.HasCheckConstraint("CK_ErrorLogEntry_Status", "\"Status\" IN ('Open', 'Acknowledged', 'Resolved')");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn().ValueGeneratedOnAdd();
        builder.Property(e => e.Level).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Category).HasMaxLength(ErrorLogEntry.CategoryMaxLength).IsRequired();
        builder.Property(e => e.EventName).HasMaxLength(ErrorLogEntry.ShortMaxLength);
        builder.Property(e => e.Message).HasMaxLength(ErrorLogEntry.MessageMaxLength).IsRequired();
        builder.Property(e => e.ExceptionType).HasMaxLength(ErrorLogEntry.ShortMaxLength);
        builder.Property(e => e.StackTrace).HasMaxLength(ErrorLogEntry.StackTraceMaxLength);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.Property(e => e.RequestPath).HasMaxLength(ErrorLogEntry.ShortMaxLength);
        builder.Property(e => e.Fingerprint).HasMaxLength(16).IsFixedLength().IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.StatusChangedBy).HasMaxLength(ErrorLogEntry.ShortMaxLength);
        builder.Property(e => e.StatusNote).HasMaxLength(1000);
        builder.Property(e => e.Version).IsRowVersion();

        string[] mutable = [nameof(ErrorLogEntry.Status), nameof(ErrorLogEntry.StatusChangedUtc), nameof(ErrorLogEntry.StatusChangedBy),
            nameof(ErrorLogEntry.StatusNote), nameof(ErrorLogEntry.Version)];
        foreach (var property in builder.Metadata.GetProperties().Where(p => !mutable.Contains(p.Name)))
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        builder.HasIndex(e => e.OccurredAtUtc);
        builder.HasIndex(e => new { e.Status, e.OccurredAtUtc });
        builder.HasIndex(e => e.Fingerprint);
        builder.HasIndex(e => e.CorrelationId);
    }
}
