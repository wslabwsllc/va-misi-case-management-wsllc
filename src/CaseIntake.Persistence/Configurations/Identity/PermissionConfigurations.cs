using CaseIntake.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.Identity;

/// <summary>Extra columns on the Identity user table (ADR 0013).</summary>
internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // Existing rows become active; the sentinel makes EF send an explicit false instead of falling back to the default.
        builder.Property(u => u.IsActive).HasDefaultValue(true).HasSentinel(true);
        builder.HasMany(u => u.PermissionOverrides).WithOne().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Extra columns on the Identity role table (ADR 0013).</summary>
internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.Property(r => r.Description).HasMaxLength(RoleFieldNames.DescriptionMaxLength);
        // Existing roles start with nothing applied, so the next seed grants a built-in role its defaults once.
        builder.Property(r => r.DefaultPermissionsApplied).HasDefaultValueSql("'{}'::text[]");
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>The permission catalog. Removing a permission from the code removes its grants (cascade).</summary>
internal sealed class AppPermissionConfiguration : IEntityTypeConfiguration<AppPermission>
{
    internal const int NameMaxLength = 128;

    public void Configure(EntityTypeBuilder<AppPermission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Name);
        builder.Property(p => p.Name).HasMaxLength(NameMaxLength);
        builder.Property(p => p.Group).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Label).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500).IsRequired();
    }
}

internal sealed class AppRolePermissionConfiguration : IEntityTypeConfiguration<AppRolePermission>
{
    public void Configure(EntityTypeBuilder<AppRolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleId, p.PermissionName });
        builder.Property(p => p.PermissionName).HasMaxLength(AppPermissionConfiguration.NameMaxLength);
        builder.HasOne<AppPermission>().WithMany().HasForeignKey(p => p.PermissionName).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => p.PermissionName);
    }
}

internal sealed class AppUserPermissionOverrideConfiguration : IEntityTypeConfiguration<AppUserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<AppUserPermissionOverride> builder)
    {
        builder.ToTable("UserPermissionOverrides", t =>
            t.HasCheckConstraint("CK_UserPermissionOverride_Effect", "\"Effect\" IN ('Allow', 'Deny')"));
        builder.HasKey(o => new { o.UserId, o.PermissionName });
        builder.Property(o => o.PermissionName).HasMaxLength(AppPermissionConfiguration.NameMaxLength);
        builder.Property(o => o.Effect).HasConversion<string>().HasMaxLength(8);
        builder.Property(o => o.ChangedById).HasMaxLength(450).IsRequired();
        builder.HasOne<AppPermission>().WithMany().HasForeignKey(o => o.PermissionName).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => o.PermissionName);
    }
}
