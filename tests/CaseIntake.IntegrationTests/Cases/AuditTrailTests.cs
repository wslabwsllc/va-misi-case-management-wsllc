using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence.Auditing;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-S4: one entry per committed change, oldest first, and no way to change or delete entries through the application.</summary>
public class AuditTrailTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    [Fact]
    public async Task Every_action_type_appends_one_entry_in_order()
    {
        var harness = new CaseTestHarness(database);
        var detail = await harness.CreateAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        detail = CaseTestHarness.Ok(await harness.Service.UpdateAsync(detail.Id, new UpdateCaseRequest("Edited title", "Edited description.", detail.Version), CancellationToken.None));
        detail = await harness.MoveAsync(detail, CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.InProgress);

        var trail = CaseTestHarness.Ok(await harness.Service.GetAuditTrailAsync(detail.Id, CancellationToken.None));

        Assert.Equal(
            [AuditActions.Created, AuditActions.Edited, AuditActions.StatusChanged, AuditActions.StatusChanged, AuditActions.StatusChanged, AuditActions.Reopened],
            trail.Select(a => a.Action));
        Assert.Equal(trail.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id).Select(a => a.Id), trail.Select(a => a.Id));
        Assert.All(trail, a => Assert.Equal(TimeSpan.Zero, a.OccurredAtUtc.Offset));
        Assert.All(trail, a => Assert.Equal("supervisor@example.test", a.Actor));
        Assert.Equal("Changed: Title, Description", trail[1].Details);
        Assert.Equal((CaseStatus.Resolved, CaseStatus.InProgress), (trail[^1].FromStatus!.Value, trail[^1].ToStatus!.Value));
        Assert.Equal(detail.UpdatedAtUtc, trail[^1].OccurredAtUtc);
    }

    [Fact]
    public async Task Rejected_operations_append_nothing()
    {
        var harness = new CaseTestHarness(database);
        var detail = await harness.CreateAsync();

        await harness.Service.UpdateAsync(detail.Id, new UpdateCaseRequest("abc", null, detail.Version), CancellationToken.None);
        await harness.Service.UpdateAsync(detail.Id, new UpdateCaseRequest(detail.Title, detail.Description, detail.Version), CancellationToken.None);   // no-op
        await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(CaseStatus.Closed, detail.Version), CancellationToken.None);
        await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(CaseStatus.Triaged, detail.Version + 1), CancellationToken.None);

        Assert.Equal(1, await harness.CountAuditAsync(detail.Id));
    }

    [Fact]
    public async Task Saved_entries_cannot_be_modified_or_deleted_through_the_context()
    {
        var detail = await new CaseTestHarness(database).CreateAsync();

        await using (var db = database.CreateContext())
        {
            var entry = await db.CaseAuditEntries.SingleAsync(a => a.CaseId == detail.Id);
            db.Entry(entry).Property(a => a.Details).CurrentValue = "tampered";
            await Assert.ThrowsAsync<AuditTrailModificationException>(() => db.SaveChangesAsync());
            Assert.Throws<AuditTrailModificationException>(() => db.SaveChanges());
        }

        await using (var db = database.CreateContext())
        {
            db.CaseAuditEntries.Remove(await db.CaseAuditEntries.SingleAsync(a => a.CaseId == detail.Id));
            await Assert.ThrowsAsync<AuditTrailModificationException>(() => db.SaveChangesAsync());
        }

        await using (var db = database.CreateContext())
        {
            // Deleting the case would cascade into the trail; the foreign key restricts it.
            db.Cases.Remove(await db.Cases.SingleAsync(c => c.Id == detail.Id));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var check = database.CreateContext();
        var stored = await check.CaseAuditEntries.SingleAsync(a => a.CaseId == detail.Id);
        Assert.Null(stored.Details);
    }

    [Fact]
    public async Task AuditAppender_adds_to_the_callers_context_without_saving()
    {
        var detail = await new CaseTestHarness(database).CreateAsync();
        await using var db = database.CreateContext();
        var entity = await db.Cases.SingleAsync(c => c.Id == detail.Id);

        var entry = AuditAppender.Append(db, entity, TestActors.CaseWorker,
            new AuditEvent(AuditActions.AttachmentUploaded, CaseTestHarness.StartTime, Details: "report.pdf"));

        Assert.Equal(EntityState.Added, db.Entry(entry).State);
        await using (var other = database.CreateContext())
        {
            Assert.Equal(1, await other.CaseAuditEntries.CountAsync(a => a.CaseId == detail.Id));
        }
        await db.SaveChangesAsync();
        await using var after = database.CreateContext();
        Assert.Equal(2, await after.CaseAuditEntries.CountAsync(a => a.CaseId == detail.Id));
        Assert.Throws<InvalidOperationException>(() => AuditAppender.Append(db, entity, FakeCurrentUser.Anonymous,
            new AuditEvent(AuditActions.Edited, CaseTestHarness.StartTime)));
    }

    [Fact]
    public async Task Audit_trail_requires_view_permission_and_an_existing_case()
    {
        var harness = new CaseTestHarness(database);
        var detail = await harness.CreateAsync();

        harness.Actor = TestActors.NoPermissions;
        CaseTestHarness.Fail(await harness.Service.GetAuditTrailAsync(detail.Id, CancellationToken.None), ErrorKind.Forbidden);
        harness.Actor = TestActors.ViewOnly;
        Assert.Single(CaseTestHarness.Ok(await harness.Service.GetAuditTrailAsync(detail.Id, CancellationToken.None)));
        CaseTestHarness.Fail(await harness.Service.GetAuditTrailAsync(long.MaxValue, CancellationToken.None), ErrorKind.NotFound);
    }
}
