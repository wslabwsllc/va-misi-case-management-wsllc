using CaseIntake.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Cases;

/// <summary>Cases table (E2-S1). Database constraints repeat the domain rules so invalid rows cannot be stored.</summary>
internal sealed class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("Cases", t =>
        {
            t.HasCheckConstraint("CK_Case_Id_Positive", "\"Id\" > 0");
            t.HasCheckConstraint("CK_Case_Title_Length",
                $"char_length(\"Title\") >= {CaseFieldLimits.TitleMinLength}");
            t.HasCheckConstraint("CK_Case_Status_Range",
                $"\"Status\" BETWEEN {(int)CaseStatus.New} AND {(int)CaseStatus.Closed}");
            t.HasCheckConstraint("CK_Case_VeteranSsnLast4_Digits",
                "\"VeteranSsnLast4\" IS NULL OR \"VeteranSsnLast4\" ~ '^[0-9]{4}$'");
            t.HasCheckConstraint("CK_Case_Veteran_Complete",
                "(\"VeteranName\" IS NULL) = (\"VeteranAddress\" IS NULL) AND (\"VeteranName\" IS NULL) = (\"VeteranSsnLast4\" IS NULL)");
            t.HasCheckConstraint("CK_Case_Priority_Range",
                $"\"Priority\" BETWEEN {(int)CasePriority.Low} AND {(int)CasePriority.Urgent}");
            t.HasCheckConstraint("CK_Case_Category_Range",
                $"\"Category\" BETWEEN {(int)CaseCategory.General} AND {(int)Enum.GetValues<CaseCategory>().Max()}");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityAlwaysColumn().ValueGeneratedOnAdd();
        builder.Property(c => c.Id).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.Property(c => c.Title).HasMaxLength(CaseFieldLimits.TitleMaxLength).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(CaseFieldLimits.DescriptionMaxLength);
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();
        builder.Property(c => c.CreatedBy).HasMaxLength(CaseFieldLimits.ActorMaxLength).IsRequired();
        builder.Property(c => c.UpdatedBy).HasMaxLength(CaseFieldLimits.ActorMaxLength).IsRequired();
        builder.Property(c => c.CreatedAtUtc).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(c => c.CreatedBy).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        // PostgreSQL xmin system column: changes on every committed update of the row (Npgsql row version).
        builder.Property(c => c.Version).IsRowVersion();

        builder.Property(c => c.VeteranName).HasMaxLength(CaseFieldLimits.VeteranNameMaxLength);
        builder.Property(c => c.VeteranAddress).HasMaxLength(CaseFieldLimits.VeteranAddressMaxLength);
        builder.Property(c => c.VeteranSsnLast4).HasMaxLength(CaseFieldLimits.SsnLast4Length).IsFixedLength();
        builder.Property(c => c.SeedKey).HasMaxLength(CaseFieldLimits.SeedKeyMaxLength);
        builder.Ignore(c => c.Veteran);
        builder.Ignore(c => c.Triage);
        builder.Ignore(c => c.IsClosed);

        // Triage fields (issue #22). The E6 migration fills them for rows that existed before.
        builder.Property(c => c.Priority).HasConversion<int>().IsRequired();
        builder.Property(c => c.Category).HasConversion<int>().IsRequired();
        builder.Property(c => c.AssignedTo).HasMaxLength(CaseFieldLimits.ActorMaxLength);
        builder.Property(c => c.DueOn).HasColumnType("date");

        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.Priority);
        builder.HasIndex(c => c.AssignedTo);
        builder.HasIndex(c => c.DueOn);
        builder.HasIndex(c => c.CreatedAtUtc);
        builder.HasIndex(c => c.UpdatedAtUtc);
        builder.HasIndex(c => c.SeedKey).IsUnique().HasFilter("\"SeedKey\" IS NOT NULL");
    }
}
