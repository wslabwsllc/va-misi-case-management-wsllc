using System.Collections.Immutable;
using System.Security.Claims;
using CaseIntake.Contracts.Security;

namespace CaseIntake.Identity.CurrentUser;

/// <summary>
/// Builds the immutable <see cref="OperationActor"/> snapshot both <see cref="ICurrentUser"/> adapters return. Permissions
/// are derived from the principal's role claims through <see cref="RolePermissionMap"/> at the time of the call, never
/// from permission claims or claims transformation.
/// </summary>
public static class OperationActorFactory
{
    public static OperationActor Anonymous { get; } =
        new(null, null, ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty);

    public static OperationActor FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true } identity)
        {
            return Anonymous;
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Anonymous;
        }

        var roleClaimType = (identity as ClaimsIdentity)?.RoleClaimType ?? ClaimTypes.Role;
        var roles = principal.Claims
            .Where(c => c.Type == roleClaimType || c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToImmutableHashSet(StringComparer.Ordinal);

        return new OperationActor(userId, identity.Name ?? userId, roles, RolePermissionMap.PermissionsFor(roles));
    }
}
