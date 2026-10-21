using System.Collections.Immutable;
using System.Security.Claims;
using CaseIntake.Contracts.Security;
using CaseIntake.Persistence;

namespace CaseIntake.Identity.CurrentUser;

/// <summary>
/// Builds the immutable <see cref="OperationActor"/> snapshot both <see cref="ICurrentUser"/> adapters return (ADR 0013):
/// the roles are the principal's trusted role claims (issued at sign-in; a role change updates the security stamp, so
/// the session is refreshed within the revalidation interval), and the permissions follow
/// <see cref="EffectivePermissions"/> from the stored role permissions and the user's stored active flag and overrides.
/// Never uses permission claims or claims transformation.
/// </summary>
public static class OperationActorFactory
{
    public static OperationActor Anonymous { get; } =
        new(null, null, ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty);

    /// <summary>The authenticated principal's Identity user ID, or <c>null</c>.</summary>
    public static string? UserId(ClaimsPrincipal? principal) =>
        principal?.Identity is { IsAuthenticated: true } && principal.FindFirst(ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } id
            ? id
            : null;

    /// <summary>The principal's role claims (empty when anonymous). Enough for showing roles; use the resolver for permissions.</summary>
    public static ImmutableHashSet<string> Roles(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true } identity)
        {
            return ImmutableHashSet<string>.Empty;
        }
        var roleClaimType = (identity as ClaimsIdentity)?.RoleClaimType ?? ClaimTypes.Role;
        return principal.Claims
            .Where(c => c.Type == roleClaimType || c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    /// <param name="principal">The request's or circuit's principal.</param>
    /// <param name="rolePermissions">Every role's permissions by name (<see cref="PermissionQueries.LoadRolePermissionsAsync"/>).</param>
    /// <param name="access">
    /// The user's stored access. <c>null</c> when the user has no row, which only happens for test principals: their
    /// role claims then count with no overrides. A deleted real user's session is rejected by revalidation.
    /// </param>
    public static OperationActor FromPrincipal(ClaimsPrincipal? principal,
        IReadOnlyDictionary<string, ImmutableHashSet<string>> rolePermissions, StoredUserAccess? access)
    {
        ArgumentNullException.ThrowIfNull(rolePermissions);
        if (UserId(principal) is not { } userId)
        {
            return Anonymous;
        }

        var roles = Roles(principal);
        var fromRoles = roles.SelectMany(r => rolePermissions.TryGetValue(r, out var granted) ? granted : []);
        var permissions = EffectivePermissions.Compute(access?.IsActive ?? true, fromRoles,
            access?.Overrides ?? ImmutableDictionary<string, PermissionEffect>.Empty);
        return new OperationActor(userId, principal!.Identity!.Name ?? userId, roles, permissions);
    }
}
