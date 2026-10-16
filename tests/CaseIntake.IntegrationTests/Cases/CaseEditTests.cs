using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-S6/E2-S7: detail retrieval, edits, immutable ID and optimistic concurrency.</summary>
public class CaseEditTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    [Fact]
    public async Task Detail_returns_every_field_and_role_filtered_next_statuses()
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync("Detail case title", "Detail description.", new VeteranSnapshot("Pat Example", "1 Example Way", "0001"));

        var asSupervisor = CaseTestHarness.Ok(await harness.Service.GetAsync(created.Id, CancellationToken.None));
        harness.Actor = TestActors.CaseWorker;
        var asCaseWorker = CaseTestHarness.Ok(await harness.Service.GetAsync(created.Id, CancellationToken.None));

        Assert.Equal(created with { AllowedNextStatuses = [] }, asSupervisor with { AllowedNextStatuses = [] });
        Assert.Equal([CaseStatus.Triaged], asSupervisor.AllowedNextStatuses);
        Assert.Empty(asCaseWorker.AllowedNextStatuses);

        var resolved = await harness.MoveAsync(created, CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved);
        harness.Actor = TestActors.Supervisor;
        Assert.Equal([CaseStatus.Closed, CaseStatus.InProgress], CaseTestHarness.Ok(await harness.Service.GetAsync(resolved.Id, CancellationToken.None)).AllowedNextStatuses);
        harness.Actor = TestActors.CaseWorker;
        Assert.Empty(CaseTestHarness.Ok(await harness.Service.GetAsync(resolved.Id, CancellationToken.None)).AllowedNextStatuses);
    }

    [Fact]
    public async Task Detail_checks_permission_before_existence()
    {
        var harness = new CaseTestHarness(database) { Actor = TestActors.NoPermissions };

        CaseTestHarness.Fail(await harness.Service.GetAsync(long.MaxValue, CancellationToken.None), ErrorKind.Forbidden);
        harness.Actor = TestActors.ViewOnly;
        Assert.Equal("CaseNotFound", CaseTestHarness.Fail(await harness.Service.GetAsync(long.MaxValue, CancellationToken.None), ErrorKind.NotFound).Code);
        CaseTestHarness.Fail(await harness.Service.GetAsync(0, CancellationToken.None), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Edit_saves_changes_refreshes_audit_fields_and_returns_a_new_version()
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync();
        harness.Actor = TestActors.CaseWorker;
        harness.Time.Advance(TimeSpan.FromHours(2));

        var edited = CaseTestHarness.Ok(await harness.Service.UpdateAsync(created.Id,
            new UpdateCaseRequest("  Updated synthetic title ", created.Description, created.Version), CancellationToken.None));

        Assert.Equal(created.Id, edited.Id);
        Assert.Equal("Updated synthetic title", edited.Title);
        Assert.Equal(created.Status, edited.Status);
        Assert.Equal(created.CreatedAtUtc, edited.CreatedAtUtc);
        Assert.Equal(created.CreatedBy, edited.CreatedBy);
        Assert.Equal(CaseTestHarness.StartTime.AddHours(2), edited.UpdatedAtUtc);
        Assert.Equal("caseworker@example.test", edited.UpdatedBy);
        Assert.NotEqual(created.Version, edited.Version);
        Assert.Equal(edited.Version, (await harness.LoadAsync(created.Id)).Version);
        var audit = CaseTestHarness.Ok(await harness.Service.GetAuditTrailAsync(created.Id, CancellationToken.None))[^1];
        Assert.Equal((AuditActions.Edited, "Changed: Title"), (audit.Action, audit.Details));
        Assert.Contains(harness.Logs.Collector.GetSnapshot(), r => r.Id.Id == 2002);
    }

    [Fact]
    public async Task Stale_version_is_a_conflict_and_nothing_is_overwritten()
    {
        var harness = new CaseTestHarness(database);
        var original = await harness.CreateAsync();
        var first = CaseTestHarness.Ok(await harness.Service.UpdateAsync(original.Id, new UpdateCaseRequest("First editor title", null, original.Version), CancellationToken.None));

        // A second browser form still holds the original version.
        var error = CaseTestHarness.Fail(await harness.Service.UpdateAsync(original.Id,
            new UpdateCaseRequest("Second editor title", null, original.Version), CancellationToken.None), ErrorKind.Conflict);

        Assert.Equal("VersionConflict", error.Code);
        var stored = await harness.LoadAsync(original.Id);
        Assert.Equal("First editor title", stored.Title);
        Assert.Equal(first.Version, stored.Version);
        Assert.Equal(2, await harness.CountAuditAsync(original.Id));
    }

    [Fact]
    public async Task Status_change_after_an_edit_with_the_old_version_is_a_conflict()
    {
        var harness = new CaseTestHarness(database);
        var original = await harness.CreateAsync();
        await harness.Service.UpdateAsync(original.Id, new UpdateCaseRequest("Edited before transition", null, original.Version), CancellationToken.None);

        CaseTestHarness.Fail(await harness.Service.ChangeStatusAsync(original.Id, new ChangeStatusRequest(CaseStatus.Triaged, original.Version), CancellationToken.None), ErrorKind.Conflict);

        Assert.Equal(CaseStatus.New, (await harness.LoadAsync(original.Id)).Status);
        Assert.Equal(2, await harness.CountAuditAsync(original.Id));
    }

    /// <summary>A transition commits between the edit's read and its save: the edit's save detects it through xmin.</summary>
    [Fact]
    public async Task Concurrent_transition_committed_during_an_edit_makes_the_edit_conflict()
    {
        var plain = new CaseTestHarness(database);
        var original = await plain.CreateAsync();
        var interleave = new InterleaveOnce(async () =>
            CaseTestHarness.Ok(await plain.Service.ChangeStatusAsync(original.Id, new ChangeStatusRequest(CaseStatus.Triaged, original.Version), CancellationToken.None)));
        var racing = new CaseTestHarness(database, interleave);

        var result = await racing.Service.UpdateAsync(original.Id, new UpdateCaseRequest("Racing edit title", null, original.Version), CancellationToken.None);

        Assert.True(interleave.Ran);
        CaseTestHarness.Fail(result, ErrorKind.Conflict);
        var stored = await plain.LoadAsync(original.Id);
        Assert.Equal((CaseStatus.Triaged, original.Title), (stored.Status, stored.Title));
        var trail = CaseTestHarness.Ok(await plain.Service.GetAuditTrailAsync(original.Id, CancellationToken.None));
        Assert.Equal([AuditActions.Created, AuditActions.StatusChanged], trail.Select(a => a.Action));   // no Edited entry committed
    }

    [Fact]
    public async Task Concurrent_edit_committed_during_a_transition_makes_the_transition_conflict()
    {
        var plain = new CaseTestHarness(database);
        var original = await plain.CreateAsync();
        var interleave = new InterleaveOnce(async () =>
            CaseTestHarness.Ok(await plain.Service.UpdateAsync(original.Id, new UpdateCaseRequest("Edit wins the race", null, original.Version), CancellationToken.None)));
        var racing = new CaseTestHarness(database, interleave);

        CaseTestHarness.Fail(await racing.Service.ChangeStatusAsync(original.Id, new ChangeStatusRequest(CaseStatus.Triaged, original.Version), CancellationToken.None), ErrorKind.Conflict);

        var stored = await plain.LoadAsync(original.Id);
        Assert.Equal((CaseStatus.New, "Edit wins the race"), (stored.Status, stored.Title));
        Assert.Equal(2, await plain.CountAuditAsync(original.Id));
    }

    [Fact]
    public async Task Two_contexts_editing_the_same_row_conflict_on_the_second_save()
    {
        var created = await new CaseTestHarness(database).CreateAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var a = await first.Cases.SingleAsync(c => c.Id == created.Id);
        var b = await second.Cases.SingleAsync(c => c.Id == created.Id);

        a.Edit("First context title", null, "a", CaseTestHarness.StartTime);
        b.Edit("Second context title", null, "b", CaseTestHarness.StartTime);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task No_op_edit_changes_neither_timestamps_version_nor_audit()
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync();
        harness.Time.Advance(TimeSpan.FromHours(1));

        var result = CaseTestHarness.Ok(await harness.Service.UpdateAsync(created.Id,
            new UpdateCaseRequest($" {created.Title} ", created.Description, created.Version), CancellationToken.None));

        Assert.Equal(created, result with { AllowedNextStatuses = created.AllowedNextStatuses });
        Assert.Equal(created.Version, (await harness.LoadAsync(created.Id)).Version);
        Assert.Equal(1, await harness.CountAuditAsync(created.Id));
    }

    [Fact]
    public async Task Edit_applies_intake_validation()
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync();

        var error = CaseTestHarness.Fail(await harness.Service.UpdateAsync(created.Id,
            new UpdateCaseRequest("   ", new string('d', 4001), created.Version), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal([CaseFieldNames.Description, CaseFieldNames.Title], error.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(created.Title, (await harness.LoadAsync(created.Id)).Title);
    }

    [Fact]
    public async Task Closed_cases_reject_edits_with_CaseClosed()
    {
        var harness = new CaseTestHarness(database);
        var closed = await harness.MoveAsync(await harness.CreateAsync(), CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed);

        var error = CaseTestHarness.Fail(await harness.Service.UpdateAsync(closed.Id,
            new UpdateCaseRequest("Edit after closing", null, closed.Version), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal("CaseClosed", error.Code);
        Assert.Equal(closed.Title, (await harness.LoadAsync(closed.Id)).Title);
    }

    [Fact]
    public async Task Edit_permission_and_existence_are_checked()
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync();

        harness.Actor = TestActors.ViewOnly;
        CaseTestHarness.Fail(await harness.Service.UpdateAsync(created.Id, new UpdateCaseRequest("Viewer edit title", null, created.Version), CancellationToken.None), ErrorKind.Forbidden);
        harness.Actor = TestActors.CaseWorker;
        CaseTestHarness.Fail(await harness.Service.UpdateAsync(long.MaxValue, new UpdateCaseRequest("Missing case title", null, 1), CancellationToken.None), ErrorKind.NotFound);
        Assert.Equal(created.Title, (await harness.LoadAsync(created.Id)).Title);
    }

    /// <summary>Runs an action once, just before the intercepted context's first SaveChanges reaches the database.</summary>
    private sealed class InterleaveOnce(Func<Task> action) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Ran)
            {
                Ran = true;
                await action();
            }
            return result;
        }
    }
}
