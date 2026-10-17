using System.Collections.Frozen;
using System.Collections.Immutable;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Security;

/// <summary>
/// The adopted role → permission matrix (plan E3-S2, recorded under OQ-3 in ADR 0007). The single source of truth for
/// the authorization handler, the <see cref="ICurrentUser"/> adapters and any module that needs to reason about roles.
/// A user with several roles gets the union of their permissions; an unknown role grants nothing.
/// </summary>
public static class RolePermissionMap
{
    private static readonly string[] Common =
    [
        Permissions.CasesView, Permissions.CasesCreate, Permissions.CasesEdit,
        Permissions.VeteranLookup,
        Permissions.AttachmentsUpload, Permissions.AttachmentsDownload,
    ];

    /// <summary>The five workflow transitions, each its own permission and authorization policy.</summary>
    public static ImmutableArray<string> TransitionPermissions { get; } =
    [
        Permissions.Transition(CaseStatus.New, CaseStatus.Triaged),
        Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),
        Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),
        Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed),
        Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress),
    ];

    /// <summary>Every permission an authorization policy is registered for: the six fixed ones plus the five transitions.</summary>
    public static ImmutableArray<string> AllPermissions { get; } = [.. Common, .. TransitionPermissions];

    private static readonly FrozenDictionary<string, ImmutableHashSet<string>> ByRole =
        new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal)
        {
            [AppRoles.CaseWorker] =
            [
                .. Common,
                Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),
                Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),
            ],
            [AppRoles.Supervisor] = [.. Common, .. TransitionPermissions],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The roles the matrix defines.</summary>
    public static ImmutableArray<string> Roles { get; } = [AppRoles.CaseWorker, AppRoles.Supervisor];

    /// <summary>Union of the permissions of <paramref name="roles"/>; unknown roles contribute nothing. Role names are case-sensitive.</summary>
    public static ImmutableHashSet<string> PermissionsFor(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var result = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (ByRole.TryGetValue(role, out var permissions))
            {
                result.UnionWith(permissions);
            }
        }
        return result.ToImmutable();
    }

    /// <summary>True when any of <paramref name="roles"/> grants <paramref name="permission"/>.</summary>
    public static bool Grants(IEnumerable<string> roles, string permission)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Any(role => ByRole.TryGetValue(role, out var permissions) && permissions.Contains(permission));
    }
}
