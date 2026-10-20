using CaseIntake.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Cases;

/// <summary>One "read up to" time per user for the case activity feed (issue #21).</summary>
internal sealed class CaseNotificationCursorConfiguration : IEntityTypeConfiguration<CaseNotificationCursor>
{
    public void Configure(EntityTypeBuilder<CaseNotificationCursor> builder)
    {
        builder.ToTable("CaseNotificationCursors");
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.UserId).HasMaxLength(CaseFieldLimits.ActorMaxLength);
        builder.Property(c => c.LastReadAtUtc).IsRequired();
    }
}
