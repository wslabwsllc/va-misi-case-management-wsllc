using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Identity.Tests;

/// <summary>
/// Golden test of the adopted matrix (plan E3-S2 / ADR 0007, QualityAnalyst added by the E5 test results module, ADR
/// 0012; Auditor and IntakeClerk added below CaseWorker; Administrator and roles.manage added by ADR 0013). The matrix is
/// the default each built-in role is seeded with; any change to it must change this table deliberately.
/// </summary>
public class RolePermissionMapTests
{
    private const bool Yes = true, No = false;

    /// <summary>Column order of the golden table.</summary>
    private static readonly string[] Columns =
        [AppRoles.Auditor, AppRoles.IntakeClerk, AppRoles.CaseWorker, AppRoles.Supervisor, AppRoles.QualityAnalyst, AppRoles.Administrator];

    /// <summary>The case roles, each granting everything the one before it does.</summary>
    private static readonly string[] CaseChain = [AppRoles.Auditor, AppRoles.IntakeClerk, AppRoles.CaseWorker, AppRoles.Supervisor];

    /// <summary>Permissions only the Administrator role holds by default.</summary>
    private static readonly string[] AdministratorOnly = [Permissions.RolesManage, Permissions.ErrorsView, Permissions.ErrorsManage, Permissions.SystemHealth];

    public static TheoryData<string, bool, bool, bool, bool, bool, bool> Matrix => new()
    {
        // permission                                                         Auditor  Intake  CaseWorker  Supervisor  QA   Admin
        { Permissions.CasesView,                                               Yes,     Yes,    Yes,        Yes,        Yes,  Yes },
        { Permissions.AttachmentsDownload,                                     Yes,     Yes,    Yes,        Yes,        No,   Yes },
        { Permissions.CasesCreate,                                             No,      Yes,    Yes,        Yes,        No,   Yes },
        { Permissions.VeteranLookup,                                           No,      Yes,    Yes,        Yes,        No,   Yes },
        { Permissions.AttachmentsUpload,                                       No,      Yes,    Yes,        Yes,        No,   Yes },
        { Permissions.CasesEdit,                                               No,      No,     Yes,        Yes,        No,   Yes },
        { Permissions.CasesAssign,                                             No,      No,     No,         Yes,        No,   Yes },
        { Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),   No,      No,     Yes,        Yes,        No,   Yes },
        { Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),  No,      No,     Yes,        Yes,        No,   Yes },
        { Permissions.Transition(CaseStatus.New, CaseStatus.Triaged),          No,      No,     No,         Yes,        No,   Yes },
        { Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed),      No,      No,     No,         Yes,        No,   Yes },
        { Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress),  No,      No,     No,         Yes,        No,   Yes },
        { Permissions.TestResultsView,                                         No,      No,     No,         No,         Yes,  Yes },
        { Permissions.TestResultsManage,                                       No,      No,     No,         No,         Yes,  Yes },
        { Permissions.UsersManage,                                             No,      No,     No,         Yes,        No,   Yes },
        { Permissions.RolesManage,                                             No,      No,     No,         No,         No,   Yes },
        { Permissions.AuditView,                                               Yes,     No,     No,         Yes,        No,   Yes },
        { Permissions.ErrorsView,                                              No,      No,     No,         No,         No,   Yes },
        { Permissions.ErrorsManage,                                            No,      No,     No,         No,         No,   Yes },
        { Permissions.SystemHealth,                                            No,      No,     No,         No,         No,   Yes },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Each_role_gets_exactly_its_listed_permissions(
        string permission, bool auditor, bool intakeClerk, bool caseWorker, bool supervisor, bool qualityAnalyst, bool administrator)
    {
        bool[] expected = [auditor, intakeClerk, caseWorker, supervisor, qualityAnalyst, administrator];
        for (var i = 0; i < Columns.Length; i++)
        {
            Assert.Equal(expected[i], RolePermissionMap.PermissionsFor([Columns[i]]).Contains(permission));
            Assert.Equal(expected[i], RolePermissionMap.Grants([Columns[i]], permission));
        }
    }

    [Fact]
    public void The_matrix_rows_are_exactly_the_registered_permissions_and_nothing_else_is_granted()
    {
        var rows = Matrix.Select(r => (string)r[0]).ToHashSet();

        Assert.Equal(20, RolePermissionMap.AllPermissions.Length);
        Assert.Equal(rows.Order(), RolePermissionMap.AllPermissions.Order());
        Assert.All(Columns, role => Assert.Subset(rows, RolePermissionMap.PermissionsFor([role])));
        Assert.Equal([3, 5, 8, 14, 3, 20], Columns.Select(role => RolePermissionMap.PermissionsFor([role]).Count).ToArray());
    }

    /// <summary>The case permissions build up along the chain; oversight (audit.view) is outside it (ADR 0014).</summary>
    [Fact]
    public void Each_case_role_adds_to_the_case_permissions_of_the_one_before_it()
    {
        static HashSet<string> CasePermissions(string role) =>
            [.. RolePermissionMap.PermissionsFor([role]).Except(RolePermissionMap.AdministrationPermissions)];

        for (var i = 1; i < CaseChain.Length; i++)
        {
            Assert.ProperSubset(CasePermissions(CaseChain[i]), CasePermissions(CaseChain[i - 1]));
        }
    }

    [Fact]
    public void Supervisor_has_every_case_permission_but_roles_and_quality_analyst_sits_outside_the_case_chain()
    {
        var supervisor = RolePermissionMap.PermissionsFor([AppRoles.Supervisor]);
        var qa = RolePermissionMap.PermissionsFor([AppRoles.QualityAnalyst]);

        Assert.Equal(RolePermissionMap.AllPermissions.Except(RolePermissionMap.TestResultsPermissions).Except(AdministratorOnly).Order(), supervisor.Order());
        Assert.Equal(RolePermissionMap.AllPermissions.Order(), RolePermissionMap.PermissionsFor([AppRoles.Administrator]).Order());
        Assert.All(CaseChain, role =>
        {
            var caseRole = RolePermissionMap.PermissionsFor([role]);
            Assert.False(qa.IsSubsetOf(caseRole), $"{role} must not hold every QualityAnalyst permission");
            Assert.False(caseRole.IsSubsetOf(qa), $"QualityAnalyst must not hold every {role} permission");
        });
    }

    [Fact]
    public void Transition_permissions_are_exactly_the_five_workflow_edges()
    {
        var edges = Enum.GetValues<CaseStatus>()
            .SelectMany(from => CaseWorkflow.Instance.NextStatuses(from).Select(to => Permissions.Transition(from, to)));

        Assert.Equal(edges.Order(), RolePermissionMap.TransitionPermissions.Order());
    }

    [Fact]
    public void Roles_lists_the_six_built_in_roles()
    {
        Assert.Equal(Columns, RolePermissionMap.Roles.ToArray());
    }

    [Theory]
    [InlineData(AppRoles.Auditor, "Auditor")]
    [InlineData(AppRoles.IntakeClerk, "Intake clerk")]
    [InlineData(AppRoles.CaseWorker, "Case worker")]
    [InlineData(AppRoles.Supervisor, "Supervisor")]
    [InlineData(AppRoles.QualityAnalyst, "Quality analyst")]
    [InlineData("Unknown", "Unknown")]
    public void Display_names_are_readable(string role, string expected)
    {
        Assert.Equal(expected, AppRoles.DisplayName(role));
    }

    [Fact]
    public void Describe_lists_display_names_in_a_stable_order_or_no_role()
    {
        Assert.Equal("no role", AppRoles.Describe([]));
        Assert.Equal("Case worker, Supervisor", AppRoles.Describe([AppRoles.Supervisor, AppRoles.CaseWorker]));
    }

    [Fact]
    public void Multiple_roles_get_the_union()
    {
        var union = RolePermissionMap.PermissionsFor([AppRoles.CaseWorker, AppRoles.Supervisor, "Unknown"]);

        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.Supervisor]), union);
        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.IntakeClerk]).Add(Permissions.AuditView), RolePermissionMap.PermissionsFor([AppRoles.Auditor, AppRoles.IntakeClerk]));
        Assert.Equal(RolePermissionMap.AllPermissions.Except(AdministratorOnly).Order(), RolePermissionMap.PermissionsFor([AppRoles.Supervisor, AppRoles.QualityAnalyst]).Order());
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("supervisor")]   // role names are case-sensitive
    [InlineData("")]
    public void Unknown_roles_grant_nothing(string role)
    {
        Assert.Empty(RolePermissionMap.PermissionsFor([role]));
        Assert.All(RolePermissionMap.AllPermissions, p => Assert.False(RolePermissionMap.Grants([role], p)));
    }

    [Fact]
    public void No_roles_grant_nothing()
    {
        Assert.Empty(RolePermissionMap.PermissionsFor([]));
    }
}

/// <summary>The permission catalog describes exactly the registered permissions, in the same order (ADR 0013).</summary>
public class PermissionCatalogTests
{
    [Fact]
    public void The_catalog_describes_every_registered_permission_in_order()
    {
        Assert.Equal(RolePermissionMap.AllPermissions, PermissionCatalog.All.Select(p => p.Name));
        Assert.All(PermissionCatalog.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Label));
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.Contains(p.Group, PermissionCatalog.Groups);
        });
        Assert.Equal(PermissionCatalog.All.Length, PermissionCatalog.All.Select(p => p.Label).Distinct().Count());
    }

    [Fact]
    public void Unknown_names_have_no_definition_and_show_as_stored()
    {
        Assert.False(PermissionCatalog.Contains("cases.delete"));
        Assert.Null(PermissionCatalog.Find("cases.delete"));
        Assert.Equal("cases.delete", PermissionCatalog.Label("cases.delete"));
        Assert.Equal("Manage roles", PermissionCatalog.Label(Permissions.RolesManage));
    }
}

/// <summary>The effective-permission rule (ADR 0013): role union, plus Allow, minus Deny; Deny wins; inactive holds nothing.</summary>
public class EffectivePermissionsTests
{
    private static readonly string[] FromRoles = [Permissions.CasesView, Permissions.CasesEdit];

    [Fact]
    public void Without_overrides_the_role_permissions_are_the_result()
    {
        Assert.Equal(FromRoles.Order(), EffectivePermissions.Compute(true, FromRoles, new Dictionary<string, PermissionEffect>()).Order());
    }

    [Fact]
    public void Allow_adds_and_deny_removes()
    {
        var result = EffectivePermissions.Compute(true, FromRoles, new Dictionary<string, PermissionEffect>
        {
            [Permissions.CasesAssign] = PermissionEffect.Allow,
            [Permissions.CasesEdit] = PermissionEffect.Deny,
        });

        Assert.Equal([Permissions.CasesAssign, Permissions.CasesView], result.Order());
    }

    [Fact]
    public void Deny_wins_over_every_role_that_grants_the_permission()
    {
        var result = EffectivePermissions.Compute(true, [.. FromRoles, Permissions.CasesView],
            new Dictionary<string, PermissionEffect> { [Permissions.CasesView] = PermissionEffect.Deny });

        Assert.DoesNotContain(Permissions.CasesView, result);
    }

    [Fact]
    public void An_inactive_user_holds_nothing_even_with_allow_overrides()
    {
        var result = EffectivePermissions.Compute(false, FromRoles,
            new Dictionary<string, PermissionEffect> { [Permissions.UsersManage] = PermissionEffect.Allow });

        Assert.Empty(result);
    }
}
