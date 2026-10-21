using System.Collections.Frozen;
using System.Collections.Immutable;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Security;

/// <summary>
/// The default role → permission matrix (plan E3-S2, recorded under OQ-3 in ADR 0007). Since ADR 0013 the database is
/// the source of truth: the seed writes this matrix into each built-in role once, after which administrators change it
/// on the Roles page. It also defines the permission names (<see cref="AllPermissions"/>, one authorization policy each)
/// and is what isolated tests use for a role's permissions. A user with several roles gets the union; an unknown role
/// grants nothing.
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

    /// <summary>The test results module (E5): viewing evidence and importing executed runs.</summary>
    public static ImmutableArray<string> TestResultsPermissions { get; } = [Permissions.TestResultsView, Permissions.TestResultsManage];

    /// <summary>Managing users and roles (ADR 0013), the audit log (ADR 0014), the error log and system health (ADR 0015).</summary>
    public static ImmutableArray<string> AdministrationPermissions { get; } =
        [Permissions.UsersManage, Permissions.RolesManage, Permissions.AuditView, Permissions.ErrorsView, Permissions.ErrorsManage, Permissions.SystemHealth];

    /// <summary>
    /// Every permission an authorization policy is registered for: the six fixed ones, assignment, the five transitions,
    /// the two test results permissions and the two administration permissions. <see cref="PermissionCatalog"/> describes
    /// each one, in the same order.
    /// </summary>
    public static ImmutableArray<string> AllPermissions { get; } =
        [.. Common, Permissions.CasesAssign, .. TransitionPermissions, .. TestResultsPermissions, .. AdministrationPermissions];

    // The case roles build on each other: Auditor ⊂ IntakeClerk ⊂ CaseWorker ⊂ Supervisor.
    private static readonly string[] ReadOnly = [Permissions.CasesView, Permissions.AttachmentsDownload];

    // Oversight: auditors review the audit log; supervisors see the history of the users they manage.
    private static readonly string[] Oversight = [Permissions.AuditView];

    private static readonly string[] Intake =
        [.. ReadOnly, Permissions.CasesCreate, Permissions.VeteranLookup, Permissions.AttachmentsUpload];

    private static readonly string[] Casework =
    [
        .. Intake, Permissions.CasesEdit,
        Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),
        Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),
    ];

    private static readonly FrozenDictionary<string, ImmutableHashSet<string>> ByRole =
        new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal)
        {
            [AppRoles.Auditor] = [.. ReadOnly, .. Oversight],
            [AppRoles.IntakeClerk] = [.. Intake],
            [AppRoles.CaseWorker] = [.. Casework],
            // Supervisors also assign and reassign case owners (case workers may only take or hand back, see CaseAssignmentRules)
            // and manage users within their own access; roles are managed by administrators.
            [AppRoles.Supervisor] = [.. Common, Permissions.CasesAssign, .. TransitionPermissions, Permissions.UsersManage, .. Oversight],
            // QA reads cases (to follow evidence back to the application) and owns test evidence; it cannot change cases.
            [AppRoles.QualityAnalyst] = [Permissions.CasesView, .. TestResultsPermissions],
            [AppRoles.Administrator] = [.. AllPermissions],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The built-in roles: seeded, never renamed or deleted (custom roles are added in the database).</summary>
    public static ImmutableArray<string> Roles { get; } =
        [AppRoles.Auditor, AppRoles.IntakeClerk, AppRoles.CaseWorker, AppRoles.Supervisor, AppRoles.QualityAnalyst, AppRoles.Administrator];

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
