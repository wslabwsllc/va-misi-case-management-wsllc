using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-S8: the five allowed edges succeed with the right permission; everything else is rejected without change.</summary>
public class CaseStatusWorkflowTests : ClassFixture<CaseStatusWorkflowTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    private static readonly Dictionary<CaseStatus, CaseStatus[]> PathTo = new()
    {
        [CaseStatus.New] = [],
        [CaseStatus.Triaged] = [CaseStatus.Triaged],
        [CaseStatus.InProgress] = [CaseStatus.Triaged, CaseStatus.InProgress],
        [CaseStatus.Resolved] = [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved],
        [CaseStatus.Closed] = [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed],
    };

    [TestCase(CaseStatus.New, CaseStatus.Triaged, AuditActions.StatusChanged)]
    [TestCase(CaseStatus.Triaged, CaseStatus.InProgress, AuditActions.StatusChanged)]
    [TestCase(CaseStatus.InProgress, CaseStatus.Resolved, AuditActions.StatusChanged)]
    [TestCase(CaseStatus.Resolved, CaseStatus.Closed, AuditActions.StatusChanged)]
    [TestCase(CaseStatus.Resolved, CaseStatus.InProgress, AuditActions.Reopened)]
    public async Task Allowed_edges_change_status_audit_fields_and_trail(CaseStatus from, CaseStatus to, string action)
    {
        var harness = new CaseTestHarness(database);
        var detail = await harness.MoveAsync(await harness.CreateAsync(), PathTo[from]);
        harness.Time.Advance(TimeSpan.FromHours(1));

        var moved = CaseTestHarness.Ok(await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(to, detail.Version), CancellationToken.None));

        Assert.Equal(to, moved.Status);
        Assert.Equal(harness.Time.GetUtcNow(), moved.UpdatedAtUtc);
        Assert.Equal("supervisor@example.test", moved.UpdatedBy);
        Assert.NotEqual(detail.Version, moved.Version);
        var last = CaseTestHarness.Ok(await harness.Service.GetAuditTrailAsync(detail.Id, CancellationToken.None))[^1];
        Assert.Equal((action, from, to), (last.Action, last.FromStatus!.Value, last.ToStatus!.Value));
        Assert.Contains(harness.Logs.Collector.GetSnapshot(), r => r.Id.Id == 2003 && r.Id.Name == "CaseStatusChanged");
    }

    public static TheoryData<CaseStatus, CaseStatus> DisallowedPairs()
    {
        var data = new TheoryData<CaseStatus, CaseStatus>();
        foreach (var from in Enum.GetValues<CaseStatus>())
        {
            foreach (var to in Enum.GetValues<CaseStatus>().Where(to => !CaseWorkflow.Instance.IsAllowed(from, to)))
            {
                data.Add(from, to);
            }
        }
        return data;
    }

    [TestCaseSource(nameof(DisallowedPairs))]
    public async Task Every_other_pair_is_an_InvalidTransition_and_changes_nothing(CaseStatus from, CaseStatus to)
    {
        var harness = new CaseTestHarness(database);
        var detail = await harness.MoveAsync(await harness.CreateAsync(), PathTo[from]);
        var auditBefore = await harness.CountAuditAsync(detail.Id);

        var error = CaseTestHarness.Fail(await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(to, detail.Version), CancellationToken.None), ErrorKind.InvalidTransition);

        Assert.Equal("InvalidTransition", error.Code);
        var stored = await harness.LoadAsync(detail.Id);
        Assert.Equal((from, detail.Version, detail.UpdatedAtUtc), (stored.Status, stored.Version, stored.UpdatedAtUtc));
        Assert.Equal(auditBefore, await harness.CountAuditAsync(detail.Id));
    }

    [Test]
    public void Disallowed_pairs_cover_20_of_25() => Assert.Equal(20, DisallowedPairs().Count());

    [TestCase(CaseStatus.New, CaseStatus.Triaged)]
    [TestCase(CaseStatus.Resolved, CaseStatus.Closed)]
    [TestCase(CaseStatus.Resolved, CaseStatus.InProgress)]
    public async Task CaseWorker_is_forbidden_supervisor_only_edges(CaseStatus from, CaseStatus to)
    {
        var harness = new CaseTestHarness(database);
        var detail = await harness.MoveAsync(await harness.CreateAsync(), PathTo[from]);
        harness.Actor = TestActors.CaseWorker;

        CaseTestHarness.Fail(await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(to, detail.Version), CancellationToken.None), ErrorKind.Forbidden);

        Assert.Equal(from, (await harness.LoadAsync(detail.Id)).Status);
        Assert.Contains(harness.Logs.Collector.GetSnapshot(), r => r.Id.Id == SecurityEvents.AuthorizationDeniedId);
    }

    [TestCase(CaseStatus.Triaged, CaseStatus.InProgress)]
    [TestCase(CaseStatus.InProgress, CaseStatus.Resolved)]
    public async Task CaseWorker_may_use_its_permitted_edges(CaseStatus from, CaseStatus to)
    {
        var harness = new CaseTestHarness(database);
        var detail = await harness.MoveAsync(await harness.CreateAsync(), PathTo[from]);
        harness.Actor = TestActors.CaseWorker;

        var moved = CaseTestHarness.Ok(await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(to, detail.Version), CancellationToken.None));

        Assert.Equal((to, "caseworker@example.test"), (moved.Status, moved.UpdatedBy));
    }

    [Test]
    public async Task Stale_version_is_a_conflict()
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync();
        var triaged = await harness.MoveAsync(created, CaseStatus.Triaged);

        // The stale form still shows New; its version predates the triage.
        CaseTestHarness.Fail(await harness.Service.ChangeStatusAsync(created.Id, new ChangeStatusRequest(CaseStatus.InProgress, created.Version), CancellationToken.None), ErrorKind.Conflict);

        var stored = await harness.LoadAsync(created.Id);
        Assert.Equal((CaseStatus.Triaged, triaged.Version), (stored.Status, stored.Version));
    }

    [TestCase(-1)]
    [TestCase(5)]
    public async Task Undefined_target_status_is_a_validation_error(int value)
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync();

        CaseTestHarness.Fail(await harness.Service.ChangeStatusAsync(created.Id, new ChangeStatusRequest((CaseStatus)value, created.Version), CancellationToken.None), ErrorKind.Validation);
    }

    [Test]
    public async Task Unknown_case_and_anonymous_actor_are_rejected()
    {
        var harness = new CaseTestHarness(database);

        CaseTestHarness.Fail(await harness.Service.ChangeStatusAsync(long.MaxValue, new ChangeStatusRequest(CaseStatus.Triaged, 1), CancellationToken.None), ErrorKind.NotFound);
        harness.Actor = TestKit.Fakes.FakeCurrentUser.Anonymous;
        CaseTestHarness.Fail(await harness.Service.ChangeStatusAsync(long.MaxValue, new ChangeStatusRequest(CaseStatus.Triaged, 1), CancellationToken.None), ErrorKind.Forbidden);
    }
}
