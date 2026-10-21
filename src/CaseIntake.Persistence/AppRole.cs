using Microsoft.AspNetCore.Identity;

namespace CaseIntake.Persistence;

/// <summary>Identity role with the permissions it grants (ADR 0013).</summary>
public class AppRole : IdentityRole
{
    public string? Description { get; set; }

    /// <summary>A role the application defines (<c>RolePermissionMap.Roles</c>): never renamed or deleted.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// The default permissions the seed has already granted this built-in role. The seed grants only defaults missing
    /// from this list, so a permission added to the defaults later reaches existing databases, while one an administrator
    /// removed is never granted again. Empty for a built-in role the seed has not handled yet, and for custom roles.
    /// </summary>
    public List<string> DefaultPermissionsApplied { get; set; } = [];

    public ICollection<AppRolePermission> Permissions { get; } = [];
}
