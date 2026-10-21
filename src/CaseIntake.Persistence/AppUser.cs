using Microsoft.AspNetCore.Identity;

namespace CaseIntake.Persistence;

/// <summary>Identity user. Owned by Persistence so AppDbContext does not depend on the Identity feature project; E3 extends behavior.</summary>
public class AppUser : IdentityUser
{
    /// <summary>An inactive user cannot sign in and holds no permissions (ADR 0013). Users are deactivated, never deleted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the account was added; <c>null</c> for accounts that existed before this column.</summary>
    public DateTimeOffset? CreatedUtc { get; set; }

    public DateTimeOffset? LastSignInUtc { get; set; }

    public DateTimeOffset? DeactivatedUtc { get; set; }

    public ICollection<AppUserPermissionOverride> PermissionOverrides { get; } = [];
}
