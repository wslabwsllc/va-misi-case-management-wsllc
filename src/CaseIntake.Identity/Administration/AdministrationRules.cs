using CaseIntake.Contracts.Security;

namespace CaseIntake.Identity.Administration;

/// <summary>
/// The no-escalation rules of ADR 0013, shared by user and role administration: nobody grants, removes or overrides a
/// permission they do not hold, manages a user who holds one, or changes their own account.
/// </summary>
internal static class AdministrationRules
{
    /// <summary>The permissions in <paramref name="permissions"/> the actor does not hold, in catalog order.</summary>
    public static IReadOnlyList<string> Missing(OperationActor actor, IEnumerable<string> permissions)
    {
        var set = permissions.ToHashSet(StringComparer.Ordinal);
        return [.. RolePermissionMap.AllPermissions.Where(p => set.Contains(p) && !actor.HasPermission(p))];
    }

    /// <summary>Why the actor may not manage the user, or <c>null</c> when they may.</summary>
    /// <param name="targetPermissions">The user's permissions as if active, so a deactivated user is judged by what reactivation would restore.</param>
    public static string? CannotManageUser(OperationActor actor, string targetUserId, IEnumerable<string> targetPermissions)
    {
        if (string.Equals(actor.UserId, targetUserId, StringComparison.Ordinal))
        {
            return "This is your own account. Ask another administrator to change your roles, status or permissions.";
        }
        var missing = Missing(actor, targetPermissions);
        return missing.Count == 0
            ? null
            : $"This person has permissions you do not have ({Labels(missing)}), so only someone with at least the same access can change their account.";
    }

    public static string Labels(IEnumerable<string> permissions) => string.Join(", ", permissions.Select(PermissionCatalog.Label));
}
