using CaseIntake.Contracts.Security;

namespace CaseIntake.Persistence;

/// <summary>A row of the permission catalog (<see cref="PermissionCatalog"/>), kept in step with the code by the seed.</summary>
public class AppPermission
{
    public required string Name { get; set; }
    public required string Group { get; set; }
    public required string Label { get; set; }
    public required string Description { get; set; }
}

/// <summary>A permission a role grants.</summary>
public class AppRolePermission
{
    public required string RoleId { get; set; }
    public required string PermissionName { get; set; }
}

/// <summary>A permission allowed or denied for one user regardless of their roles; a denial wins (except the veteran lookup for anyone who can create cases; see <c>EffectivePermissions</c>).</summary>
public class AppUserPermissionOverride
{
    public required string UserId { get; set; }
    public required string PermissionName { get; set; }
    public PermissionEffect Effect { get; set; }
    public DateTimeOffset ChangedUtc { get; set; }

    /// <summary>The Identity user ID of the administrator who set the override.</summary>
    public required string ChangedById { get; set; }
}
