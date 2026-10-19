using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Identity.Tests;

/// <summary>
/// Golden test of the adopted matrix (plan E3-S2 / ADR 0007, QualityAnalyst added by the E5 test results module, ADR
/// 0012). Any change to who may do what must change this table deliberately.
/// </summary>
public class RolePermissionMapTests
{
    private const bool Yes = true, No = false;

    public static TheoryData<string, bool, bool, bool> Matrix => new()
    {
        // permission                                                         CaseWorker  Supervisor  QualityAnalyst
        { Permissions.CasesView,                                               Yes,        Yes,        Yes },
        { Permissions.CasesCreate,                                             Yes,        Yes,        No },
        { Permissions.CasesEdit,                                               Yes,        Yes,        No },
        { Permissions.VeteranLookup,                                           Yes,        Yes,        No },
        { Permissions.AttachmentsUpload,                                       Yes,        Yes,        No },
        { Permissions.AttachmentsDownload,                                     Yes,        Yes,        No },
        { Permissions.Transition(CaseStatus.New, CaseStatus.Triaged),          No,         Yes,        No },
        { Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),   Yes,        Yes,        No },
        { Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),  Yes,        Yes,        No },
        { Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed),      No,         Yes,        No },
        { Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress),  No,         Yes,        No },
        { Permissions.TestResultsView,                                         No,         No,         Yes },
        { Permissions.TestResultsManage,                                       No,         No,         Yes },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Each_role_gets_exactly_its_listed_permissions(string permission, bool caseWorker, bool supervisor, bool qualityAnalyst)
    {
        Assert.Equal(caseWorker, RolePermissionMap.PermissionsFor([AppRoles.CaseWorker]).Contains(permission));
        Assert.Equal(supervisor, RolePermissionMap.PermissionsFor([AppRoles.Supervisor]).Contains(permission));
        Assert.Equal(qualityAnalyst, RolePermissionMap.PermissionsFor([AppRoles.QualityAnalyst]).Contains(permission));
        Assert.Equal(caseWorker, RolePermissionMap.Grants([AppRoles.CaseWorker], permission));
        Assert.Equal(supervisor, RolePermissionMap.Grants([AppRoles.Supervisor], permission));
        Assert.Equal(qualityAnalyst, RolePermissionMap.Grants([AppRoles.QualityAnalyst], permission));
    }

    [Fact]
    public void The_matrix_rows_are_exactly_the_registered_permissions_and_nothing_else_is_granted()
    {
        var rows = Matrix.Select(r => (string)r[0]).ToHashSet();

        Assert.Equal(13, RolePermissionMap.AllPermissions.Length);
        Assert.Equal(rows.Order(), RolePermissionMap.AllPermissions.Order());
        Assert.Subset(rows, RolePermissionMap.PermissionsFor([AppRoles.CaseWorker]));
        Assert.Subset(rows, RolePermissionMap.PermissionsFor([AppRoles.Supervisor]));
        Assert.Subset(rows, RolePermissionMap.PermissionsFor([AppRoles.QualityAnalyst]));
        Assert.Equal(8, RolePermissionMap.PermissionsFor([AppRoles.CaseWorker]).Count);
        Assert.Equal(11, RolePermissionMap.PermissionsFor([AppRoles.Supervisor]).Count);
        Assert.Equal(3, RolePermissionMap.PermissionsFor([AppRoles.QualityAnalyst]).Count);
    }

    [Fact]
    public void Transition_permissions_are_exactly_the_five_workflow_edges()
    {
        var edges = Enum.GetValues<CaseStatus>()
            .SelectMany(from => CaseWorkflow.Instance.NextStatuses(from).Select(to => Permissions.Transition(from, to)));

        Assert.Equal(edges.Order(), RolePermissionMap.TransitionPermissions.Order());
    }

    [Fact]
    public void The_roles_differ()
    {
        Assert.NotEqual(RolePermissionMap.PermissionsFor([AppRoles.CaseWorker]), RolePermissionMap.PermissionsFor([AppRoles.Supervisor]));
        Assert.NotEqual(RolePermissionMap.PermissionsFor([AppRoles.Supervisor]), RolePermissionMap.PermissionsFor([AppRoles.QualityAnalyst]));
        Assert.Equal([AppRoles.CaseWorker, AppRoles.Supervisor, AppRoles.QualityAnalyst], RolePermissionMap.Roles.ToArray());
    }

    [Fact]
    public void Multiple_roles_get_the_union()
    {
        var union = RolePermissionMap.PermissionsFor([AppRoles.CaseWorker, AppRoles.Supervisor, "Unknown"]);

        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.Supervisor]), union);
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
