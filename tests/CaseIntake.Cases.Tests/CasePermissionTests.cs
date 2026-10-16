using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit.Fakes;

namespace CaseIntake.Cases.Tests;

/// <summary>
/// AllowedNextStatuses = workflow edges filtered by the actor's transition permissions (E2-S6). The role permission
/// sets below mirror the adopted matrix (plan E3-S2); E3 owns the real map.
/// </summary>
public class CasePermissionTests
{
    private static readonly string[] Common =
        [Permissions.CasesView, Permissions.CasesCreate, Permissions.CasesEdit];

    private static OperationActor CaseWorker { get; } = FakeCurrentUser.Authenticated("cw", "caseworker@example.test", AppRoles.CaseWorker,
    [
        .. Common,
        Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),
        Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),
    ]);

    private static OperationActor Supervisor { get; } = FakeCurrentUser.Authenticated("sv", "supervisor@example.test", AppRoles.Supervisor,
    [
        .. Common,
        Permissions.Transition(CaseStatus.New, CaseStatus.Triaged),
        Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),
        Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),
        Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed),
        Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress),
    ]);

    [Theory]
    [InlineData(CaseStatus.New, new CaseStatus[0], new[] { CaseStatus.Triaged })]
    [InlineData(CaseStatus.Triaged, new[] { CaseStatus.InProgress }, new[] { CaseStatus.InProgress })]
    [InlineData(CaseStatus.InProgress, new[] { CaseStatus.Resolved }, new[] { CaseStatus.Resolved })]
    [InlineData(CaseStatus.Resolved, new CaseStatus[0], new[] { CaseStatus.Closed, CaseStatus.InProgress })]
    [InlineData(CaseStatus.Closed, new CaseStatus[0], new CaseStatus[0])]
    public void Allowed_next_statuses_differ_by_role(CaseStatus status, CaseStatus[] caseWorker, CaseStatus[] supervisor)
    {
        Assert.Equal(caseWorker, CaseService.AllowedNextStatuses(status, CaseWorker, CaseWorkflow.Instance));
        Assert.Equal(supervisor, CaseService.AllowedNextStatuses(status, Supervisor, CaseWorkflow.Instance));
    }

    [Fact]
    public void Permissions_for_disallowed_edges_never_expose_them()
    {
        // Holding every transition permission (including unlisted pairs) still yields only workflow edges.
        var everything = new FakeCurrentUser().Actor;

        Assert.Equal([CaseStatus.Triaged], CaseService.AllowedNextStatuses(CaseStatus.New, everything, CaseWorkflow.Instance));
        Assert.Empty(CaseService.AllowedNextStatuses(CaseStatus.Closed, everything, CaseWorkflow.Instance));
    }

    [Fact]
    public void Anonymous_actor_gets_no_next_statuses()
    {
        Assert.Empty(CaseService.AllowedNextStatuses(CaseStatus.New, FakeCurrentUser.Anonymous, CaseWorkflow.Instance));
    }
}
