using CaseIntake.Cases;
using CaseIntake.Cases.Seeding;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Migrations;
using CaseIntake.Persistence.Seeding;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>Identity rows for the triage tests: two people who work cases and an intake clerk who does not.</summary>
internal static class TriageIdentity
{
    public static readonly OperationActor IntakeClerk = FakeCurrentUser.ForRole(AppRoles.IntakeClerk, "ic-id");

    public static async Task SeedAsync(PostgresDatabaseFixture database)
    {
        await using var db = database.CreateContext();
        foreach (var role in new[] { AppRoles.CaseWorker, AppRoles.Supervisor, AppRoles.IntakeClerk })
        {
            db.Roles.Add(new AppRole { Id = $"role-{role}", Name = role, NormalizedName = role.ToUpperInvariant() });
        }
        void User(string id, string name, string role)
        {
            db.Users.Add(new AppUser { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name, NormalizedEmail = name.ToUpperInvariant() });
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = $"role-{role}" });
        }
        User("cw-id", "caseworker@example.test", AppRoles.CaseWorker);
        User("sv-id", "supervisor@example.test", AppRoles.Supervisor);
        User("ic-id", "intakeclerk@example.test", AppRoles.IntakeClerk);
        await db.SaveChangesAsync();
    }
}

/// <summary>Issue #22: priority, category, owner and due date through the real case service, directory and database.</summary>
[Category("Story:Issue-22")]
public sealed class CaseTriageTests : ClassFixture<CaseTriageTests, PostgresDatabaseFixture>, IAsyncLifetime
{
    private static PostgresDatabaseFixture database => Shared;

    private readonly CaseTestHarness _harness = new(database);

    [SetUp]

    public async Task InitializeAsync()
    {
        await using var db = database.CreateContext();
        if (!await db.Users.AnyAsync())
        {
            await TriageIdentity.SeedAsync(database);
        }
    }

    [TearDown]

    public Task DisposeAsync() => Task.CompletedTask;

    private CaseAssigneeDirectory Directory() => new(_harness.Factory, _harness.CurrentUser, new FakeLogger<CaseAssigneeDirectory>());

    [Test]
    public async Task Intake_without_triage_is_medium_general_unassigned_and_due_after_the_medium_target()
    {
        var created = await _harness.CreateAsync("Defaults at intake");

        Assert.Equal((CasePriority.Medium, CaseCategory.General, (string?)null), (created.Priority, created.Category, created.AssignedTo));
        Assert.Equal(new DateOnly(2026, 10, 26), created.DueOn);   // 2026-10-16 + 10 days
        var stored = await _harness.LoadAsync(created.Id);
        Assert.Equal(created.DueOn, stored.DueOn);
    }

    [Test]
    public async Task An_empty_due_date_follows_the_priority_and_an_explicit_one_is_kept()
    {
        _harness.Actor = TestActors.Supervisor;   // choosing an owner at intake needs cases.assign
        var urgent = CaseTestHarness.Ok(await _harness.Service.CreateAsync(
            new CreateCaseRequest("Urgent intake", null, null, new CaseTriage(CasePriority.Urgent, CaseCategory.Claims, null, null)), CancellationToken.None));
        var explicitDue = CaseTestHarness.Ok(await _harness.Service.CreateAsync(
            new CreateCaseRequest("Explicit due", null, null, new CaseTriage(CasePriority.Low, CaseCategory.Records, " caseworker@example.test ", new DateOnly(2027, 1, 15))), CancellationToken.None));

        Assert.Equal(new DateOnly(2026, 10, 18), urgent.DueOn);
        Assert.Equal(new DateOnly(2027, 1, 15), explicitDue.DueOn);
        Assert.Equal("caseworker@example.test", explicitDue.AssignedTo);   // trimmed
        Assert.Equal(CaseCategory.Records, explicitDue.Category);
    }

    [Test]
    public async Task Only_supervisors_choose_an_owner_at_intake_and_only_people_who_work_cases_can_own_one()
    {
        _harness.Actor = TestActors.CaseWorker;
        var workerAssigns = await _harness.Service.CreateAsync(new CreateCaseRequest("Worker assigns at intake", null, null,
            new CaseTriage(CasePriority.High, CaseCategory.Billing, "caseworker@example.test", null)), CancellationToken.None);
        Assert.Equal("Your role cannot choose the owner of a case.", workerAssigns.Error!.FieldErrors![CaseFieldNames.AssignedTo].Single());

        _harness.Actor = TriageIdentity.IntakeClerk;
        var clerkAssigns = await _harness.Service.CreateAsync(new CreateCaseRequest("Clerk assigns", null, null,
            new CaseTriage(CasePriority.High, CaseCategory.Billing, "caseworker@example.test", null)), CancellationToken.None);
        var clerkLeavesOpen = await _harness.Service.CreateAsync(new CreateCaseRequest("Clerk leaves owner empty", null, null,
            new CaseTriage(CasePriority.High, CaseCategory.Billing, null, null)), CancellationToken.None);

        _harness.Actor = TestActors.Supervisor;
        var toClerk = await _harness.Service.CreateAsync(new CreateCaseRequest("Assigned to a clerk", null, null,
            new CaseTriage(CasePriority.Medium, CaseCategory.General, "intakeclerk@example.test", null)), CancellationToken.None);
        var toNobody = await _harness.Service.CreateAsync(new CreateCaseRequest("Assigned to nobody known", null, null,
            new CaseTriage(CasePriority.Medium, CaseCategory.General, "ghost@example.test", null)), CancellationToken.None);

        Assert.Equal("Your role cannot choose the owner of a case.", clerkAssigns.Error!.FieldErrors![CaseFieldNames.AssignedTo].Single());
        Assert.True(clerkLeavesOpen.IsSuccess);
        Assert.Equal("Choose an owner who can work cases.", toClerk.Error!.FieldErrors![CaseFieldNames.AssignedTo].Single());
        Assert.Equal(ErrorKind.Validation, toNobody.Error!.Kind);
    }

    [Test]
    public async Task Editing_triage_is_audited_by_field_never_changes_the_owner_and_a_null_triage_leaves_it_unchanged()
    {
        var created = await _harness.CreateAsync("Triage edit");
        _harness.Actor = TestActors.CaseWorker;
        _harness.Time.Advance(TimeSpan.FromMinutes(5));

        // The edit asks for a new owner too; only Assign may change it, so the owner stays.
        var edited = CaseTestHarness.Ok(await _harness.Service.UpdateAsync(created.Id, new UpdateCaseRequest(created.Title, created.Description, created.Version,
            new CaseTriage(CasePriority.Urgent, CaseCategory.General, "supervisor@example.test", created.DueOn)), CancellationToken.None));
        var textOnly = CaseTestHarness.Ok(await _harness.Service.UpdateAsync(created.Id,
            new UpdateCaseRequest(created.Title + " (text)", created.Description, edited.Version), CancellationToken.None));

        Assert.Equal((CasePriority.Urgent, (string?)null), (edited.Priority, edited.AssignedTo));
        Assert.Equal((CasePriority.Urgent, (string?)null), (textOnly.Priority, textOnly.AssignedTo));
        var trail = CaseTestHarness.Ok(await _harness.Service.GetAuditTrailAsync(created.Id, CancellationToken.None));
        Assert.Equal("Changed: Priority", trail[^2].Details);
        Assert.Equal("Changed: Title", trail[^1].Details);
    }

    // ---------- Assignment (supervisors assign; case workers take or hand back) ----------

    private Task<Result<CaseDetailDto>> AssignAsync(CaseDetailDto c, string? owner) =>
        _harness.Service.AssignAsync(c.Id, new AssignCaseRequest(owner, c.Version), CancellationToken.None);

    [Test]
    public async Task Supervisor_assigns_reassigns_and_unassigns_with_audited_details()
    {
        var created = await _harness.CreateAsync("Supervisor assigns");
        _harness.Actor = TestActors.Supervisor;

        var toWorker = CaseTestHarness.Ok(await AssignAsync(created, "caseworker@example.test"));
        var toSupervisor = CaseTestHarness.Ok(await AssignAsync(toWorker, "supervisor@example.test"));
        var unassigned = CaseTestHarness.Ok(await AssignAsync(toSupervisor, null));

        Assert.Equal(("caseworker@example.test", "supervisor@example.test", (string?)null), (toWorker.AssignedTo, toSupervisor.AssignedTo, unassigned.AssignedTo));
        var trail = CaseTestHarness.Ok(await _harness.Service.GetAuditTrailAsync(created.Id, CancellationToken.None));
        Assert.Equal(
            ["Assigned to caseworker@example.test", "Assigned to supervisor@example.test (was caseworker@example.test)", "Unassigned (was supervisor@example.test)"],
            trail.Where(a => a.Action == AuditActions.Assigned).Select(a => a.Details));
        Assert.Contains(_harness.Logs.Collector.GetSnapshot(), r => r.Id.Id == 2004 && r.Message.Contains($"Case {created.Id} owner changed", StringComparison.Ordinal));
    }

    [Test]
    public async Task Case_worker_takes_an_unassigned_case_and_hands_back_only_their_own()
    {
        var created = await _harness.CreateAsync("Take me");
        _harness.Actor = TestActors.CaseWorker;

        var taken = CaseTestHarness.Ok(await AssignAsync(created, "caseworker@example.test"));
        Assert.Equal("caseworker@example.test", taken.AssignedTo);
        var handedBack = CaseTestHarness.Ok(await AssignAsync(taken, null));
        Assert.Null(handedBack.AssignedTo);

        // Assigning someone else, or taking a case someone else owns, is a supervisor's job.
        Assert.Equal(ErrorKind.Forbidden, (await AssignAsync(handedBack, "supervisor@example.test")).Error!.Kind);
        _harness.Actor = TestActors.Supervisor;
        var theirs = CaseTestHarness.Ok(await AssignAsync(handedBack, "supervisor@example.test"));
        _harness.Actor = TestActors.CaseWorker;
        Assert.Equal(ErrorKind.Forbidden, (await AssignAsync(theirs, "caseworker@example.test")).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await AssignAsync(theirs, null)).Error!.Kind);
    }

    [Test]
    public async Task Assignment_is_refused_for_clerks_closed_cases_unknown_owners_and_stale_versions()
    {
        var created = await _harness.CreateAsync("Refusals");
        _harness.Actor = TriageIdentity.IntakeClerk;
        Assert.Equal(ErrorKind.Forbidden, (await AssignAsync(created, "intakeclerk@example.test")).Error!.Kind);

        _harness.Actor = TestActors.Supervisor;
        Assert.Equal("Choose an owner who can work cases.",
            (await AssignAsync(created, "intakeclerk@example.test")).Error!.FieldErrors![CaseFieldNames.AssignedTo].Single());
        var assigned = CaseTestHarness.Ok(await AssignAsync(created, "caseworker@example.test"));
        Assert.Equal(ErrorKind.Conflict, (await AssignAsync(created, "supervisor@example.test")).Error!.Kind);   // created.Version is stale

        var closed = await _harness.MoveAsync(assigned, CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed);
        Assert.Equal("CaseClosed", (await AssignAsync(closed, null)).Error!.Code);
    }

    [Test]
    public async Task Keeping_the_current_owner_is_allowed_even_if_they_no_longer_work_cases()
    {
        var created = await _harness.CreateAsync("Former owner");
        await using (var db = database.CreateContext())
        {
            // An owner set before their role changed (simulated directly in the database).
            await db.Database.ExecuteSqlRawAsync("UPDATE \"Cases\" SET \"AssignedTo\" = 'intakeclerk@example.test' WHERE \"Id\" = {0}", created.Id);
        }
        var current = CaseTestHarness.Ok(await _harness.Service.GetAsync(created.Id, CancellationToken.None));

        var result = await _harness.Service.UpdateAsync(created.Id, new UpdateCaseRequest(current.Title, "Still theirs.", current.Version, current.Triage), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("intakeclerk@example.test", result.Value!.AssignedTo);
    }

    [Test]
    public async Task Assignee_directory_lists_the_people_who_work_cases()
    {
        _harness.Actor = TriageIdentity.IntakeClerk;   // reading the list only needs cases.view
        var result = CaseTestHarness.Ok(await Directory().ListAsync(CancellationToken.None));

        Assert.Equal([new AssigneeDto("caseworker@example.test", AppRoles.CaseWorker), new AssigneeDto("supervisor@example.test", AppRoles.Supervisor)], result);

        _harness.Actor = TestActors.NoPermissions;
        Assert.Equal(ErrorKind.Forbidden, (await Directory().ListAsync(CancellationToken.None)).Error!.Kind);
    }

    [Test]
    public async Task Invalid_triage_values_are_rejected_before_the_database()
    {
        var result = await _harness.Service.CreateAsync(new CreateCaseRequest("Bad triage", null, null,
            new CaseTriage((CasePriority)9, (CaseCategory)99, null, new DateOnly(1999, 12, 31))), CancellationToken.None);

        Assert.Equal([CaseFieldNames.Category, CaseFieldNames.DueOn, CaseFieldNames.Priority], result.Error!.FieldErrors!.Keys.Order());
    }
}

/// <summary>Issue #21: the activity feed, unread counts and the per-user read cursor, over the real audit trail.</summary>
[Category("Story:Issue-21")]
public sealed class CaseNotificationTests : IAsyncLifetime
{
    private readonly PostgresDatabaseFixture _database = new();
    private CaseTestHarness _harness = null!;

    [SetUp]

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        await TriageIdentity.SeedAsync(_database);
        _harness = new CaseTestHarness(_database);
    }

    [TearDown]

    public Task DisposeAsync() => _database.DisposeAsync();

    private CaseNotificationService Notifications() => new(_harness.Factory, _harness.CurrentUser, _harness.Time,
        Options.Create(new CaseNotificationOptions()), new FakeLogger<CaseNotificationService>());

    private async Task<CaseNotificationFeed> FeedAsAsync(OperationActor actor)
    {
        _harness.Actor = actor;
        return CaseTestHarness.Ok(await Notifications().GetFeedAsync(50, CancellationToken.None));
    }

    [Test]
    public async Task Activity_by_others_is_unread_until_marked_and_own_activity_never_notifies()
    {
        // The supervisor creates a case, assigns it to the case worker and triages it: three entries.
        var created = await _harness.CreateAsync("Notify me");
        _harness.Time.Advance(TimeSpan.FromMinutes(1));
        var assigned = CaseTestHarness.Ok(await _harness.Service.AssignAsync(created.Id,
            new AssignCaseRequest("caseworker@example.test", created.Version), CancellationToken.None));
        await _harness.MoveAsync(assigned, CaseStatus.Triaged);

        var worker = await FeedAsAsync(TestActors.CaseWorker);
        Assert.Equal(3, worker.UnreadCount);
        Assert.Equal([AuditActions.StatusChanged, AuditActions.Assigned, AuditActions.Created], worker.Items.Select(i => i.Action));
        Assert.All(worker.Items, i => Assert.True(i.Unread));
        Assert.All(worker.Items, i => Assert.True(i.AssignedToMe));
        Assert.Equal("Assigned to caseworker@example.test", worker.Items[1].Details);
        Assert.Equal([false, true, false], worker.Items.Select(i => i.AssignedToYou));   // the assignment itself
        Assert.Equal("Notify me", worker.Items[0].CaseTitle);
        Assert.Null(worker.LastReadAtUtc);

        Assert.Empty((await FeedAsAsync(TestActors.Supervisor)).Items);   // the supervisor's own actions

        _harness.Actor = TestActors.CaseWorker;
        var read = CaseTestHarness.Ok(await Notifications().MarkAllReadAsync(CancellationToken.None));
        Assert.Equal(0, read.UnreadCount);
        Assert.Equal(_harness.Time.GetUtcNow(), read.LastReadAtUtc);
        Assert.All(read.Items, i => Assert.False(i.Unread));

        _harness.Time.Advance(TimeSpan.FromMinutes(2));
        _harness.Actor = TestActors.Supervisor;
        var latest = CaseTestHarness.Ok(await _harness.Service.GetAsync(created.Id, CancellationToken.None));
        CaseTestHarness.Ok(await _harness.Service.UpdateAsync(created.Id, new UpdateCaseRequest(created.Title, "New note.", latest.Version), CancellationToken.None));

        _harness.Actor = TestActors.CaseWorker;
        Assert.Equal(1, CaseTestHarness.Ok(await Notifications().GetUnreadCountAsync(CancellationToken.None)));
        var after = await FeedAsAsync(TestActors.CaseWorker);
        Assert.Equal([true, false, false, false], after.Items.Select(i => i.Unread));
    }

    [Test]
    public async Task Marking_twice_moves_the_cursor_forward_only_and_old_activity_leaves_the_window()
    {
        await _harness.CreateAsync("Old news");
        _harness.Actor = TestActors.CaseWorker;
        CaseTestHarness.Ok(await Notifications().MarkAllReadAsync(CancellationToken.None));
        var first = (await FeedAsAsync(TestActors.CaseWorker)).LastReadAtUtc;
        _harness.Actor = TestActors.CaseWorker;
        CaseTestHarness.Ok(await Notifications().MarkAllReadAsync(CancellationToken.None));   // same time: no error, no change
        Assert.Equal(first, (await FeedAsAsync(TestActors.CaseWorker)).LastReadAtUtc);

        _harness.Time.Advance(TimeSpan.FromDays(15));
        Assert.Empty((await FeedAsAsync(TestActors.CaseWorker)).Items);
        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.CaseNotificationCursors.CountAsync());
    }

    [Test]
    public async Task One_notification_can_be_marked_read_on_its_own_and_mark_all_then_covers_it()
    {
        var created = await _harness.CreateAsync("Read one at a time");
        _harness.Time.Advance(TimeSpan.FromMinutes(1));
        await _harness.MoveAsync(created, CaseStatus.Triaged);
        var feed = await FeedAsAsync(TestActors.CaseWorker);
        Assert.Equal(2, feed.UnreadCount);

        _harness.Actor = TestActors.CaseWorker;
        var afterOne = CaseTestHarness.Ok(await Notifications().MarkReadAsync(feed.Items[1].Id, CancellationToken.None));
        Assert.Equal(1, afterOne.UnreadCount);
        Assert.Equal([true, false], afterOne.Items.Select(i => i.Unread));
        Assert.Equal(1, CaseTestHarness.Ok(await Notifications().GetUnreadCountAsync(CancellationToken.None)));
        CaseTestHarness.Ok(await Notifications().MarkReadAsync(feed.Items[1].Id, CancellationToken.None));   // again: no error, no duplicate

        // Someone else's feed is not affected.
        await using (var db = _database.CreateContext())
        {
            Assert.Equal(1, await db.CaseNotificationReads.CountAsync());
        }

        _harness.Actor = TestActors.CaseWorker;
        var all = CaseTestHarness.Ok(await Notifications().MarkAllReadAsync(CancellationToken.None));
        Assert.Equal(0, all.UnreadCount);
        await using var after = _database.CreateContext();
        Assert.Equal(0, await after.CaseNotificationReads.CountAsync());   // the cursor covers them now
    }

    [Test]
    public async Task Only_an_entry_of_your_own_feed_can_be_marked_read()
    {
        var created = await _harness.CreateAsync("Not yours to mark");
        var supervisorEntry = (await FeedAsAsync(TestActors.CaseWorker)).Items.Single().Id;

        // The supervisor wrote the entry, so it is not in the supervisor's feed.
        _harness.Actor = TestActors.Supervisor;
        Assert.Equal(ErrorKind.NotFound, (await Notifications().MarkReadAsync(supervisorEntry, CancellationToken.None)).Error!.Kind);
        _harness.Actor = TestActors.CaseWorker;
        Assert.Equal(ErrorKind.NotFound, (await Notifications().MarkReadAsync(long.MaxValue, CancellationToken.None)).Error!.Kind);
        _harness.Actor = TestActors.NoPermissions;
        Assert.Equal(ErrorKind.Forbidden, (await Notifications().MarkReadAsync(supervisorEntry, CancellationToken.None)).Error!.Kind);
        Assert.NotNull(created);
    }

    [Test]
    public async Task Without_cases_view_the_feed_is_forbidden()
    {
        _harness.Actor = TestActors.NoPermissions;
        Assert.Equal(ErrorKind.Forbidden, (await Notifications().GetFeedAsync(10, CancellationToken.None)).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await Notifications().GetUnreadCountAsync(CancellationToken.None)).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await Notifications().MarkAllReadAsync(CancellationToken.None)).Error!.Kind);
    }
}

/// <summary>The E5 triage migration gives seeded rows of existing databases exactly what a fresh seed gives them.</summary>
[Category("Story:Issue-22")]
public sealed class CaseTriageBackfillTests : IAsyncLifetime
{
    private readonly PostgresDatabaseFixture _database = new();

    [SetUp]

    public Task InitializeAsync() => _database.InitializeAsync();

    [TearDown]

    public Task DisposeAsync() => _database.DisposeAsync();

    [Test]
    public async Task Backfill_sql_matches_the_seed_triage_formula()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_database.CreateContextFactory());
        services.AddCasesSeed();
        await using (var provider = services.BuildServiceProvider())
        {
            await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);
        }

        await using var db = _database.CreateContext();
        // A database seeded before the migration: the new columns hold their defaults.
        await db.Database.ExecuteSqlRawAsync("UPDATE \"Cases\" SET \"Priority\" = 1, \"Category\" = 0, \"AssignedTo\" = NULL, \"DueOn\" = NULL");
        await db.Database.ExecuteSqlRawAsync(E5_CaseTriageAndNotifications.BackfillSql);

        var cases = await db.Cases.AsNoTracking().Where(c => c.SeedKey != null).OrderBy(c => c.SeedKey).ToListAsync();
        Assert.Equal(CaseSeedData.CaseCount, cases.Count);
        for (var i = 0; i < cases.Count; i++)
        {
            Assert.Equal(CaseSeedData.TriageFor(i), cases[i].Triage);
        }
        // The seed spreads due dates, so on the demo date some open cases are overdue and some are not.
        var demoDay = new DateOnly(2026, 10, 20);
        var open = cases.Where(c => c.Status is CaseStatus.New or CaseStatus.Triaged or CaseStatus.InProgress).ToList();
        Assert.InRange(open.Count(c => CaseTriageRules.IsOverdue(c.Status, c.DueOn, demoDay)), 1, open.Count - 1);
    }
}
