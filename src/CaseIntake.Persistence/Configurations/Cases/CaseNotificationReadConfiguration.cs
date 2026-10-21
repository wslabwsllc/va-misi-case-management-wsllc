using CaseIntake.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Cases;

/// <summary>Notifications a user marked as read one by one (redesign); the key prevents duplicates.</summary>
internal sealed class CaseNotificationReadConfiguration : IEntityTypeConfiguration<CaseNotificationRead>
{
    public void Configure(EntityTypeBuilder<CaseNotificationRead> builder)
    {
        builder.ToTable("CaseNotificationReads");
        builder.HasKey(r => new { r.UserId, r.AuditEntryId });
        builder.Property(r => r.UserId).HasMaxLength(CaseFieldLimits.ActorMaxLength);
        builder.Property(r => r.ReadAtUtc).IsRequired();
    }
}
