using System.Net;
using System.Text;
using CaseIntake.Cases;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Administration;
using CaseIntake.Identity.Auditing;
using CaseIntake.Identity.Seeding;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using CaseIntake.TestKit.Fakes;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>
/// ADR 0014 end to end: what the audit log records (authentication, access denials, user and role administration, cases),
/// that entries commit with their change, carry the operation ID, cannot be changed, and that the viewer and CSV export
/// filter, merge and authorize correctly.
/// </summary>
public class AuditLogTests : ClassFixture<AuditLogTests, IdentityWebFixture>
{
    private static IdentityWebFixture fixture => Shared;

    private const string Password = "Initial-Passw0rd!";

    private IServiceProvider Services => fixture.Factory.Services;

    private AuditLogService AuditAs(OperationActor actor) => new(
        Services.GetRequiredService<IDbContextFactory<AppDbContext>>(), new FakeCurrentUser { Actor = actor },
        Services.GetRequiredService<AuditLogWriter>(), Services.GetRequiredService<TimeProvider>(),
        Services.GetRequiredService<ILogger<AuditLogService>>());

    private AuditLogService Audit => AuditAs(AdministrationTestSupport.Administrator);

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private async Task<UserDetailDto> CreateUserAsync(string email, params string[] roles)
    {
        var users = AdministrationTestSupport.Users(Services, AdministrationTestSupport.Administrator);
        var created = await users.CreateAsync(new CreateUserRequest(email, roles, Password), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return (await users.GetAsync(created.Value!.Id, CancellationToken.None)).Value!;
    }

    private async Task<IReadOnlyList<AuditLogEntryDto>> AboutAsync(string entityType, string entityId) =>
        (await Audit.ListAsync(new AuditQuery(EntityType: entityType, EntityId: entityId, OldestFirst: true, PageSize: 200), CancellationToken.None)).Value!.Items;

    [Test]
    public async Task User_administration_is_recorded_with_before_and_after_values_and_one_operation_id_per_change()
    {
        var user = await CreateUserAsync(NewEmail("audited"), AppRoles.Auditor);
        var users = AdministrationTestSupport.Users(Services, AdministrationTestSupport.Administrator);
        var updated = (await users.UpdateAsync(user.Id, new UpdateUserRequest(user.UserName, [AppRoles.CaseWorker], user.ConcurrencyStamp), CancellationToken.None)).Value!;
        var overridden = (await users.SetOverridesAsync(user.Id, new SetPermissionOverridesRequest(
            new Dictionary<string, PermissionEffect> { [Permissions.CasesEdit] = PermissionEffect.Deny }, updated.ConcurrencyStamp), CancellationToken.None)).Value!;
        await users.SetStatusAsync(user.Id, new SetUserStatusRequest(false, overridden.ConcurrencyStamp), CancellationToken.None);

        var entries = await AboutAsync(AuditEntityTypes.User, user.Id);

        Assert.Equal([AuditLogActions.UserCreated, AuditLogActions.UserUpdated, AuditLogActions.UserPermissionsChanged, AuditLogActions.UserDeactivated],
            entries.Select(e => e.Action));
        Assert.All(entries, e =>
        {
            Assert.Equal(AuditCategories.Users, e.Category);
            Assert.Equal(DemoUsers.Administrator.Id, e.ActorId);
            Assert.Equal(user.UserName, e.EntityName);
            Assert.False(string.IsNullOrEmpty(e.CorrelationId));
        });
        Assert.Equal(4, entries.Select(e => e.CorrelationId).Distinct().Count());
        Assert.Equal(new AuditChangeDto("Roles", AppRoles.Auditor, AppRoles.CaseWorker), entries[1].Changes.Single());
        Assert.Equal(new AuditChangeDto(Permissions.CasesEdit, "Inherit", "Deny"), entries[2].Changes.Single());
        Assert.Equal(new AuditChangeDto("Active", "Yes", "No"), entries[3].Changes.Single());
        Assert.DoesNotContain(entries, e => e.Changes.Any(c => (c.Before + c.After).Contains(Password, StringComparison.Ordinal)));
    }

    [Test]
    public async Task A_refused_change_writes_no_audit_entry()
    {
        var user = await CreateUserAsync(NewEmail("refused"), AppRoles.Auditor);
        var users = AdministrationTestSupport.Users(Services, AdministrationTestSupport.Administrator);

        var stale = await users.UpdateAsync(user.Id, new UpdateUserRequest(user.UserName, [AppRoles.CaseWorker], "stale-stamp"), CancellationToken.None);
        var escalation = await AdministrationTestSupport.Users(Services, AdministrationTestSupport.Supervisor)
            .UpdateAsync(user.Id, new UpdateUserRequest(user.UserName, [AppRoles.Administrator], user.ConcurrencyStamp), CancellationToken.None);

        Assert.Equal(ErrorKind.Conflict, stale.Error!.Kind);
        Assert.Equal(ErrorKind.Validation, escalation.Error!.Kind);
        Assert.Equal([AuditLogActions.UserCreated], (await AboutAsync(AuditEntityTypes.User, user.Id)).Select(e => e.Action));
    }

    [Test]
    public async Task Role_changes_are_recorded_and_a_deleted_role_keeps_its_history()
    {
        var roles = AdministrationTestSupport.Roles(Services, AdministrationTestSupport.Administrator);
        var role = (await roles.CreateAsync(new SaveRoleRequest($"Audited {Guid.NewGuid():N}"[..20], "First", [Permissions.CasesView]), CancellationToken.None)).Value!;
        var updated = (await roles.UpdateAsync(role.Id, new SaveRoleRequest(role.Name, "Second", [Permissions.CasesView, Permissions.AttachmentsDownload], role.ConcurrencyStamp), CancellationToken.None)).Value!;
        await roles.DeleteAsync(role.Id, updated.ConcurrencyStamp, CancellationToken.None);

        var entries = await AboutAsync(AuditEntityTypes.Role, role.Id);

        Assert.Equal([AuditLogActions.RoleCreated, AuditLogActions.RoleUpdated, AuditLogActions.RoleDeleted], entries.Select(e => e.Action));
        Assert.Equal(["Description", "Permissions"], entries[1].Changes.Select(c => c.Field));
        Assert.Equal("cases.view, attachments.download", entries[1].Changes[1].After);   // stable names, not labels
        Assert.Equal(role.Name, entries[2].Changes.Single(c => c.Field == "Name").Before);
    }

    [Test]
    public async Task Sign_in_outcomes_sign_out_and_password_changes_are_recorded_with_the_request_operation_id()
    {
        var email = NewEmail("signin");
        var user = await CreateUserAsync(email, AppRoles.CaseWorker);

        using (var wrong = fixture.NewSession())
        {
            using var response = await wrong.LoginAsync(email, "Wrong-Passw0rd!");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var unknown = fixture.NewSession();
        using (await unknown.LoginAsync(NewEmail("nobody"), "Typed-Secret-1!"))
        {
        }
        using var session = fixture.NewSession();
        using var login = await session.LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var operationId = login.Headers.GetValues("X-Operation-Id").Single();
        using (await session.LogoutAsync())
        {
        }

        var entries = await AboutAsync(AuditEntityTypes.User, user.Id);
        Assert.Equal([AuditLogActions.UserCreated, AuditLogActions.SignInFailed, AuditLogActions.SignedIn, AuditLogActions.SignedOut], entries.Select(e => e.Action));
        Assert.Equal(AuditOutcome.Failure, entries[1].Outcome);
        Assert.Equal(operationId, entries[2].CorrelationId);
        var anonymous = (await Audit.ListAsync(new AuditQuery(Category: AuditCategories.Authentication, Search: "unknown account"), CancellationToken.None)).Value!.Items;
        Assert.Contains(anonymous, e => e.ActorId is null && e.ActorName is null);
        await using var db = fixture.CreateContext();
        Assert.False(await db.AuditLogEntries.AnyAsync(e => (e.Summary ?? "").Contains("Typed-Secret") || (e.ActorName ?? "").Contains("nobody")));
    }

    [Test]
    public async Task An_http_access_denial_is_recorded_with_the_page_and_missing_permission()
    {
        using var session = fixture.NewSession();
        await session.SignInAsync(DemoUsers.CaseWorker.UserName);

        using var denied = await session.Client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        var entries = (await Audit.ListAsync(new AuditQuery(Category: AuditCategories.Authorization, EntityId: "/admin/users"), CancellationToken.None)).Value!.Items;
        var entry = Assert.Single(entries, e => e.ActorId == DemoUsers.CaseWorker.Id);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Contains("Manage users", entry.Summary);
    }

    [Test]
    public async Task Case_changes_appear_in_the_log_with_status_changes_and_a_correlation_id()
    {
        var actor = FakeCurrentUser.ForRole(AppRoles.Supervisor, DemoUsers.Supervisor.Id);
        var cases = ActivatorUtilities.CreateInstance<CaseService>(Services, new FakeCurrentUser { Actor = actor });
        var created = (await cases.CreateAsync(new CreateCaseRequest($"Audit log case {Guid.NewGuid():N}", null, null), CancellationToken.None)).Value!;
        await cases.ChangeStatusAsync(created.Id, new ChangeStatusRequest(Domain.Cases.CaseStatus.Triaged, created.Version), CancellationToken.None);

        var entries = await AboutAsync(AuditEntityTypes.Case, created.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(["Created", "StatusChanged"], entries.Select(e => e.Action));
        Assert.All(entries, e => Assert.StartsWith("C-", e.Key, StringComparison.Ordinal));
        Assert.Equal(new AuditChangeDto("Status", "New", "Triaged"), entries[1].Changes.Single());
        Assert.All(entries, e => Assert.False(string.IsNullOrEmpty(e.CorrelationId)));
        Assert.Equal(entries[1].Key, (await Audit.GetAsync(entries[1].Key, CancellationToken.None)).Value!.Key);
    }

    [Test]
    public async Task The_viewer_merges_sources_in_time_order_pages_and_filters_by_eastern_dates()
    {
        var from = new DateOnly(2001, 1, 1);
        await using (var db = fixture.CreateContext())
        {
            // 23:30 ET on Jan 1 is 04:30 UTC on Jan 2.
            SystemAudit.Append(db, new AuditRecord(AuditCategories.Users, "Probe", AuditOutcome.Success, "x", "probe-actor", AuditEntityTypes.User, "probe"),
                new DateTimeOffset(2001, 1, 2, 4, 30, 0, TimeSpan.Zero));
            SystemAudit.Append(db, new AuditRecord(AuditCategories.Users, "Probe", AuditOutcome.Success, "x", "probe-actor", AuditEntityTypes.User, "probe"),
                new DateTimeOffset(2001, 1, 2, 5, 30, 0, TimeSpan.Zero));
            await db.SaveChangesAsync();
        }

        var janFirst = (await Audit.ListAsync(new AuditQuery(from, from, Actor: "PROBE-actor"), CancellationToken.None)).Value!;
        var both = (await Audit.ListAsync(new AuditQuery(from, from.AddDays(1), Actor: "probe-actor", PageSize: 1), CancellationToken.None)).Value!;
        var second = (await Audit.ListAsync(new AuditQuery(from, from.AddDays(1), Actor: "probe-actor", PageSize: 1, Page: 2), CancellationToken.None)).Value!;
        var tooDeep = await Audit.ListAsync(new AuditQuery(Page: 101, PageSize: 100), CancellationToken.None);
        var badRange = await Audit.ListAsync(new AuditQuery(from.AddDays(2), from), CancellationToken.None);

        Assert.Equal(1, janFirst.TotalCount);
        Assert.Equal(2, both.TotalCount);
        Assert.True(both.Items.Single().OccurredAtUtc > second.Items.Single().OccurredAtUtc);
        Assert.Equal(ErrorKind.Validation, tooDeep.Error!.Kind);
        Assert.Equal(ErrorKind.Validation, badRange.Error!.Kind);

        var all = (await Audit.ListAsync(new AuditQuery(PageSize: 200), CancellationToken.None)).Value!.Items;
        Assert.Equal(all.OrderByDescending(e => e.OccurredAtUtc).Select(e => e.Key), all.Select(e => e.Key));
    }

    [Test]
    public async Task Only_audit_view_reads_the_log_and_entries_cannot_be_changed_or_deleted()
    {
        var clerk = FakeCurrentUser.ForRole(AppRoles.IntakeClerk);
        Assert.Equal(ErrorKind.Forbidden, (await AuditAs(clerk).ListAsync(new AuditQuery(), CancellationToken.None)).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await AuditAs(clerk).ExportAsync(new AuditQuery(), CancellationToken.None)).Error!.Kind);
        Assert.True((await AuditAs(FakeCurrentUser.ForRole(AppRoles.Auditor)).ListAsync(new AuditQuery(), CancellationToken.None)).IsSuccess);

        await CreateUserAsync(NewEmail("immutable"), AppRoles.Auditor);
        await using var db = fixture.CreateContext();
        var entry = await db.AuditLogEntries.FirstAsync();
        db.AuditLogEntries.Remove(entry);
        await Assert.ThrowsAsync<AuditTrailModificationException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task The_csv_export_is_authorized_formula_safe_in_utc_and_itself_audited()
    {
        await using (var db = fixture.CreateContext())
        {
            SystemAudit.Append(db, new AuditRecord(AuditCategories.Users, "Probe", AuditOutcome.Success, "x", "=HYPERLINK(\"x\")", Summary: "csv probe"),
                new DateTimeOffset(2002, 3, 4, 5, 6, 7, TimeSpan.Zero));
            await db.SaveChangesAsync();
        }
        using var admin = fixture.NewSession();
        await admin.SignInAsync(DemoUsers.Administrator.UserName);
        using var clerk = fixture.NewSession();
        await clerk.SignInAsync(DemoUsers.IntakeClerk.UserName);

        using var response = await admin.Client.GetAsync("/exports/audit.csv?from=2002-03-01&to=2002-03-31");
        using var forbidden = await clerk.Client.GetAsync("/exports/audit.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("\uFEFFKey,OccurredAtUtc,", csv, StringComparison.Ordinal);
        Assert.Contains("2002-03-04T05:06:07Z", csv);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var exports = (await Audit.ListAsync(new AuditQuery(Category: AuditCategories.AuditLog), CancellationToken.None)).Value!.Items;
        Assert.Contains(exports, e => e.ActorId == DemoUsers.Administrator.Id && e.Changes.Any(c => c.Field == "From" && c.After == "2002-03-01"));
    }

    [Test]
    public async Task A_default_added_later_reaches_seeded_roles_but_a_removed_one_stays_removed()
    {
        await using (var db = fixture.CreateContext())
        {
            // A database seeded before audit.view existed: Supervisor had neither the grant nor the applied entry.
            var supervisor = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Name == AppRoles.Supervisor);
            db.RolePermissions.RemoveRange(supervisor.Permissions.Where(p => p.PermissionName == Permissions.AuditView));
            supervisor.DefaultPermissionsApplied = [.. supervisor.DefaultPermissionsApplied.Where(p => p != Permissions.AuditView)];
            await db.SaveChangesAsync();
        }

        await IdentityTestSupport.SeedAsync(fixture.ConnectionString);
        var roles = AdministrationTestSupport.Roles(Services, AdministrationTestSupport.Administrator);
        var supervisorId = (await roles.ListAsync(CancellationToken.None)).Value!.Single(r => r.Name == AppRoles.Supervisor).Id;
        var afterSeed = (await roles.GetAsync(supervisorId, CancellationToken.None)).Value!;
        Assert.Contains(Permissions.AuditView, afterSeed.Permissions);

        var removed = await roles.UpdateAsync(supervisorId, new SaveRoleRequest(afterSeed.Name, afterSeed.Description,
            [.. afterSeed.Permissions.Where(p => p != Permissions.AuditView)], afterSeed.ConcurrencyStamp), CancellationToken.None);
        Assert.True(removed.IsSuccess, removed.Error?.Message);
        await IdentityTestSupport.SeedAsync(fixture.ConnectionString);
        var afterReseed = (await roles.GetAsync(supervisorId, CancellationToken.None)).Value!;
        Assert.DoesNotContain(Permissions.AuditView, afterReseed.Permissions);

        // Restore for the other tests in this class.
        await roles.UpdateAsync(supervisorId, new SaveRoleRequest(afterReseed.Name, afterReseed.Description,
            [.. afterReseed.Permissions, Permissions.AuditView], afterReseed.ConcurrencyStamp), CancellationToken.None);
    }
}
