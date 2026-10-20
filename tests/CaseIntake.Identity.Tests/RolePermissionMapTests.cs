using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Identity.Tests;

/// <summary>
/// Golden test of the adopted matrix (plan E3-S2 / ADR 0007, QualityAnalyst added by the E5 test results module, ADR
/// 0012; Auditor and IntakeClerk added below CaseWorker). Any change to who may do what must change this table deliberately.
/// </summary>
public class RolePermissionMapTests
{
    private const bool Yes = true, No = false;

    /// <summary>Column order of the golden table.</summary>
    private static readonly string[] Columns =
        [AppRoles.Auditor, AppRoles.IntakeClerk, AppRoles.CaseWorker, AppRoles.Supervisor, AppRoles.QualityAnalyst];

    /// <summary>The case roles, each granting everything the one before it does.</summary>
    private static readonly string[] CaseChain = [AppRoles.Auditor, AppRoles.IntakeClerk, AppRoles.CaseWorker, AppRoles.Supervisor];

    public static TheoryData<string, bool, bool, bool, bool, bool> Matrix => new()
    {
        // permission                                                         Auditor  Intake  CaseWorker  Supervisor  QA
        { Permissions.CasesView,                                               Yes,     Yes,    Yes,        Yes,        Yes },
        { Permissions.AttachmentsDownload,                                     Yes,     Yes,    Yes,        Yes,        No },
        { Permissions.CasesCreate,                                             No,      Yes,    Yes,        Yes,        No },
        { Permissions.VeteranLookup,                                           No,      Yes,    Yes,        Yes,        No },
        { Permissions.AttachmentsUpload,                                       No,      Yes,    Yes,        Yes,        No },
        { Permissions.CasesEdit,                                               No,      No,     Yes,        Yes,        No },
        { Permissions.CasesAssign,                                             No,      No,     No,         Yes,        No },
        { Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),   No,      No,     Yes,        Yes,        No },
        { Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),  No,      No,     Yes,        Yes,        No },
        { Permissions.Transition(CaseStatus.New, CaseStatus.Triaged),          No,      No,     No,         Yes,        No },
        { Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed),      No,      No,     No,         Yes,        No },
        { Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress),  No,      No,     No,         Yes,        No },
        { Permissions.TestResultsView,                                         No,      No,     No,         No,         Yes },
        { Permissions.TestResultsManage,                                       No,      No,     No,         No,         Yes },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Each_role_gets_exactly_its_listed_permissions(
        string permission, bool auditor, bool intakeClerk, bool caseWorker, bool supervisor, bool qualityAnalyst)
    {
        bool[] expected = [auditor, intakeClerk, caseWorker, supervisor, qualityAnalyst];
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

        Assert.Equal(14, RolePermissionMap.AllPermissions.Length);
        Assert.Equal(rows.Order(), RolePermissionMap.AllPermissions.Order());
        Assert.All(Columns, role => Assert.Subset(rows, RolePermissionMap.PermissionsFor([role])));
        Assert.Equal([2, 5, 8, 12, 3], Columns.Select(role => RolePermissionMap.PermissionsFor([role]).Count).ToArray());
    }

    [Fact]
    public void Each_case_role_adds_to_the_one_before_it()
    {
        for (var i = 1; i < CaseChain.Length; i++)
        {
            Assert.ProperSubset(
                RolePermissionMap.PermissionsFor([CaseChain[i]]).ToHashSet(),
                RolePermissionMap.PermissionsFor([CaseChain[i - 1]]).ToHashSet());
        }
    }

    [Fact]
    public void Supervisor_has_every_case_permission_and_quality_analyst_sits_outside_the_case_chain()
    {
        var supervisor = RolePermissionMap.PermissionsFor([AppRoles.Supervisor]);
        var qa = RolePermissionMap.PermissionsFor([AppRoles.QualityAnalyst]);

        Assert.Equal(RolePermissionMap.AllPermissions.Except(RolePermissionMap.TestResultsPermissions).Order(), supervisor.Order());
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
    public void Roles_lists_the_five_application_roles()
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
        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.IntakeClerk]), RolePermissionMap.PermissionsFor([AppRoles.Auditor, AppRoles.IntakeClerk]));
        Assert.Equal(RolePermissionMap.AllPermissions.Order(), RolePermissionMap.PermissionsFor([AppRoles.Supervisor, AppRoles.QualityAnalyst]).Order());
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
