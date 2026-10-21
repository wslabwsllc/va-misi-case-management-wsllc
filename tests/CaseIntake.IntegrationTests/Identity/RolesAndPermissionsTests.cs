using System.Net;
using CaseIntake.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Administration;
using CaseIntake.Identity.Authorization;
using CaseIntake.Identity.Seeding;
using CaseIntake.Persistence;
using CaseIntake.TestKit.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>Builds the administration services over a host's real database, Identity and permission resolver.</summary>
internal static class AdministrationTestSupport
{
    public static UserAdministrationService Users(IServiceProvider services, OperationActor actor) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
        new FakeCurrentUser { Actor = actor },
        services.GetRequiredService<PermissionResolver>(),
        services.GetRequiredService<TimeProvider>(),
        services.GetRequiredService<ILogger<UserAdministrationService>>());

    public static RoleAdministrationService Roles(IServiceProvider services, OperationActor actor) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
        new FakeCurrentUser { Actor = actor },
        services.GetRequiredService<PermissionResolver>(),
        services.GetRequiredService<TimeProvider>(),
        services.GetRequiredService<ILogger<RoleAdministrationService>>());

    /// <summary>The seeded demo administrator as an actor with every permission.</summary>
    public static OperationActor Administrator { get; } =
        FakeCurrentUser.ForRole(AppRoles.Administrator, DemoUsers.Administrator.Id);

    public static OperationActor Supervisor { get; } =
        FakeCurrentUser.ForRole(AppRoles.Supervisor, DemoUsers.Supervisor.Id);
}

/// <summary>
/// ADR 0013 end to end: database-backed role permissions, per-user overrides and deactivation take effect in the running
/// app, and administration stays within the actor's own access.
/// </summary>
public class RolesAndPermissionsTests : ClassFixture<RolesAndPermissionsTests, IdentityWebFixture>
{
    private static IdentityWebFixture fixture => Shared;

    private const string Password = "Initial-Passw0rd!";

    private IServiceProvider Services => fixture.Factory.Services;
    private UserAdministrationService UsersAs(OperationActor actor) => AdministrationTestSupport.Users(Services, actor);
    private RoleAdministrationService RolesAs(OperationActor actor) => AdministrationTestSupport.Roles(Services, actor);

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private async Task<UserDetailDto> CreateAsync(string email, params string[] roles)
    {
        var created = await UsersAs(AdministrationTestSupport.Administrator).CreateAsync(new CreateUserRequest(email, roles, Password), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return (await UsersAs(AdministrationTestSupport.Administrator).GetAsync(created.Value!.Id, CancellationToken.None)).Value!;
    }

    [Test]
    public async Task The_seed_writes_the_catalog_and_each_built_in_roles_defaults_and_administrator_gets_everything()
    {
        await using var db = fixture.CreateContext();

        Assert.Equal(RolePermissionMap.AllPermissions.Order(), (await db.Permissions.Select(p => p.Name).ToListAsync()).Order());
        var roles = await db.Roles.Include(r => r.Permissions).Where(r => r.IsBuiltIn).ToListAsync();
        Assert.Equal(RolePermissionMap.Roles.Order(), roles.Select(r => r.Name!).Order());
        Assert.All(roles, r =>
        {
            Assert.Equal(RolePermissionMap.PermissionsFor([r.Name!]).Order(), r.DefaultPermissionsApplied.Order());
            Assert.Equal(RolePermissionMap.PermissionsFor([r.Name!]).Order(), r.Permissions.Select(p => p.PermissionName).Order());
        });
        Assert.NotNull(await db.Users.SingleOrDefaultAsync(u => u.UserName == DemoUsers.Administrator.UserName));
    }

    [Test]
    public async Task Reseeding_keeps_an_administrators_change_to_a_built_in_role()
    {
        var auditor = (await RolesAs(AdministrationTestSupport.Administrator).ListAsync(CancellationToken.None)).Value!.Single(r => r.Name == AppRoles.Auditor);
        var detail = (await RolesAs(AdministrationTestSupport.Administrator).GetAsync(auditor.Id, CancellationToken.None)).Value!;
        var updated = await RolesAs(AdministrationTestSupport.Administrator).UpdateAsync(auditor.Id,
            new SaveRoleRequest(detail.Name, detail.Description, [Permissions.CasesView], detail.ConcurrencyStamp), CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);

        await IdentityTestSupport.SeedAsync(fixture.ConnectionString);

        var after = (await RolesAs(AdministrationTestSupport.Administrator).GetAsync(auditor.Id, CancellationToken.None)).Value!;
        Assert.Equal([Permissions.CasesView], after.Permissions);

        // Restore the default for the other tests in this class.
        await RolesAs(AdministrationTestSupport.Administrator).UpdateAsync(auditor.Id,
            new SaveRoleRequest(after.Name, after.Description, [.. RolePermissionMap.PermissionsFor([AppRoles.Auditor])], after.ConcurrencyStamp), CancellationToken.None);
    }

    [Test]
    public async Task A_custom_roles_permissions_apply_to_its_members_at_the_next_request()
    {
        var created = await RolesAs(AdministrationTestSupport.Administrator).CreateAsync(
            new SaveRoleRequest($"Reviewer {Guid.NewGuid():N}"[..20], "Reads cases.", [Permissions.CasesView]), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var role = created.Value!;
        var email = NewEmail("reviewer");
        await CreateAsync(email, role.Name);
        using var session = fixture.NewSession();
        await session.SignInAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/cases")).StatusCode);

        var updated = await RolesAs(AdministrationTestSupport.Administrator).UpdateAsync(role.Id,
            new SaveRoleRequest(role.Name, role.Description, [Permissions.AttachmentsDownload], role.ConcurrencyStamp), CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);

        using var denied = await session.Client.GetAsync("/cases");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("http://localhost/account/access-denied", denied.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3021 && Equals(r.GetStructuredStateValue("RoleId"), role.Id));
    }

    [Test]
    public async Task Allow_and_deny_overrides_change_what_one_user_may_do()
    {
        var email = NewEmail("override");
        var user = await CreateAsync(email, AppRoles.CaseWorker);
        using var session = fixture.NewSession();
        await session.SignInAsync(email, Password);

        var result = await UsersAs(AdministrationTestSupport.Administrator).SetOverridesAsync(user.Id, new SetPermissionOverridesRequest(
            new Dictionary<string, PermissionEffect>
            {
                [Permissions.CasesView] = PermissionEffect.Deny,
                [Permissions.UsersManage] = PermissionEffect.Allow,
            }, user.ConcurrencyStamp), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var detail = result.Value!;
        Assert.False(detail.Permissions.Single(p => p.Permission == Permissions.CasesView).Granted);
        Assert.Equal([AppRoles.CaseWorker], detail.Permissions.Single(p => p.Permission == Permissions.CasesView).FromRoles);
        Assert.True(detail.Permissions.Single(p => p.Permission == Permissions.UsersManage).Granted);
        using var cases = await session.Client.GetAsync("/cases");
        Assert.Equal(HttpStatusCode.Redirect, cases.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/admin/users")).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(DemoUsers.Administrator.Id, (await db.UserPermissionOverrides.FirstAsync(o => o.UserId == user.Id)).ChangedById);
    }

    [Test]
    public async Task A_deactivated_user_loses_access_at_once_is_signed_out_and_cannot_sign_in_until_reactivated()
    {
        var email = NewEmail("leaver");
        var user = await CreateAsync(email, AppRoles.CaseWorker);
        using var session = fixture.NewSession();
        await session.SignInAsync(email, Password);

        var deactivated = await UsersAs(AdministrationTestSupport.Administrator).SetStatusAsync(user.Id, new SetUserStatusRequest(false, user.ConcurrencyStamp), CancellationToken.None);

        Assert.True(deactivated.IsSuccess, deactivated.Error?.Message);
        Assert.False(deactivated.Value!.IsActive);
        Assert.NotNull(deactivated.Value.DeactivatedUtc);
        Assert.All(deactivated.Value.Permissions, p => Assert.False(p.Granted));
        using (var now = await session.Client.GetAsync("/cases"))
        {
            Assert.Equal(HttpStatusCode.Redirect, now.StatusCode);   // denied immediately, signed out at the next revalidation
        }
        var signedOut = false;
        for (var attempt = 0; attempt < 60 && !signedOut; attempt++)
        {
            await Task.Delay(250);
            using var response = await session.Client.GetAsync("/cases");
            signedOut = response.Headers.Location?.ToString().StartsWith("http://localhost/account/login", StringComparison.Ordinal) == true;
        }
        Assert.True(signedOut);

        using var fresh = fixture.NewSession();
        using var refused = await fresh.LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);   // the form again, with the generic error
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3002 && Equals(r.GetStructuredStateValue("Reason"), "Inactive"));

        var reactivated = await UsersAs(AdministrationTestSupport.Administrator).SetStatusAsync(user.Id,
            new SetUserStatusRequest(true, deactivated.Value.ConcurrencyStamp), CancellationToken.None);
        Assert.True(reactivated.IsSuccess, reactivated.Error?.Message);
        await fresh.SignInAsync(email, Password);
        await using var db = fixture.CreateContext();
        Assert.NotNull((await db.Users.SingleAsync(u => u.Id == user.Id)).LastSignInUtc);
    }

    [Test]
    public async Task Email_and_roles_are_changed_together_and_a_stale_stamp_is_a_conflict()
    {
        var user = await CreateAsync(NewEmail("edit"), AppRoles.Auditor);
        var newEmail = NewEmail("edited");

        var updated = await UsersAs(AdministrationTestSupport.Administrator).UpdateAsync(user.Id,
            new UpdateUserRequest(newEmail, [AppRoles.CaseWorker, AppRoles.QualityAnalyst], user.ConcurrencyStamp), CancellationToken.None);
        var stale = await UsersAs(AdministrationTestSupport.Administrator).UpdateAsync(user.Id,
            new UpdateUserRequest(newEmail, [AppRoles.Auditor], user.ConcurrencyStamp), CancellationToken.None);

        Assert.True(updated.IsSuccess, updated.Error?.Message);
        Assert.Equal(newEmail, updated.Value!.UserName);
        Assert.Equal([AppRoles.CaseWorker, AppRoles.QualityAnalyst], updated.Value.Roles);
        Assert.Equal(ErrorKind.Conflict, stale.Error!.Kind);
        Assert.Equal([AppRoles.CaseWorker, AppRoles.QualityAnalyst], (await UsersAs(AdministrationTestSupport.Administrator).GetAsync(user.Id, CancellationToken.None)).Value!.Roles);
    }

    [Test]
    public async Task Nobody_grants_more_than_they_hold_manages_someone_with_more_or_changes_their_own_account()
    {
        var supervisor = UsersAs(AdministrationTestSupport.Supervisor);
        var clerk = await CreateAsync(NewEmail("clerk"), AppRoles.IntakeClerk);
        var admin = (await supervisor.GetAsync(DemoUsers.Administrator.Id, CancellationToken.None)).Value!;
        var self = (await supervisor.GetAsync(DemoUsers.Supervisor.Id, CancellationToken.None)).Value!;

        var grantAdmin = await supervisor.CreateAsync(new CreateUserRequest(NewEmail("x"), [AppRoles.Administrator], Password), CancellationToken.None);
        var allowQa = await supervisor.SetOverridesAsync(clerk.Id, new SetPermissionOverridesRequest(
            new Dictionary<string, PermissionEffect> { [Permissions.TestResultsView] = PermissionEffect.Allow }, clerk.ConcurrencyStamp), CancellationToken.None);
        var deactivateAdmin = await supervisor.SetStatusAsync(admin.Id, new SetUserStatusRequest(false, admin.ConcurrencyStamp), CancellationToken.None);
        var promoteSelf = await supervisor.UpdateAsync(self.Id, new UpdateUserRequest(self.UserName, [AppRoles.Supervisor, AppRoles.QualityAnalyst], self.ConcurrencyStamp), CancellationToken.None);

        Assert.Contains("Administrator", grantAdmin.Error!.FieldErrors![UserFieldNames.Roles].Single());
        Assert.Contains("View test results", allowQa.Error!.FieldErrors![UserFieldNames.Overrides].Single());
        Assert.False(admin.CanManage);
        Assert.Equal(ErrorKind.Forbidden, deactivateAdmin.Error!.Kind);
        Assert.False(self.CanManage);
        Assert.Contains("your own account", promoteSelf.Error!.Message);
        Assert.True((await UsersAs(AdministrationTestSupport.Administrator).GetAsync(DemoUsers.Administrator.Id, CancellationToken.None)).Value!.IsActive);

        var options = (await supervisor.ListRoleOptionsAsync(CancellationToken.None)).Value!;
        Assert.False(options.Single(o => o.Name == AppRoles.Administrator).Assignable);
        Assert.True(options.Single(o => o.Name == AppRoles.CaseWorker).Assignable);
    }

    [Test]
    public async Task Role_rules_protect_built_in_roles_members_and_the_ability_to_manage_roles()
    {
        var roles = RolesAs(AdministrationTestSupport.Administrator);
        var all = (await roles.ListAsync(CancellationToken.None)).Value!;
        var adminRole = (await roles.GetAsync(all.Single(r => r.Name == AppRoles.Administrator).Id, CancellationToken.None)).Value!;
        var supervisorRole = (await roles.GetAsync(all.Single(r => r.Name == AppRoles.Supervisor).Id, CancellationToken.None)).Value!;

        var changeAdmin = await roles.UpdateAsync(adminRole.Id, new SaveRoleRequest(adminRole.Name, null, [Permissions.CasesView], adminRole.ConcurrencyStamp), CancellationToken.None);
        var deleteBuiltIn = await roles.DeleteAsync(supervisorRole.Id, supervisorRole.ConcurrencyStamp, CancellationToken.None);
        var supervisorAddsQa = await RolesAs(AdministrationTestSupport.Supervisor).CreateAsync(
            new SaveRoleRequest("Sneaky", null, [Permissions.TestResultsView]), CancellationToken.None);
        var duplicate = await roles.CreateAsync(new SaveRoleRequest("caseworker", null, []), CancellationToken.None);

        Assert.False(adminRole.CanManage);
        Assert.Equal(ErrorKind.Forbidden, changeAdmin.Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, deleteBuiltIn.Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, supervisorAddsQa.Error!.Kind);   // the supervisor lacks roles.manage altogether
        Assert.Equal(["A role with this name already exists."], duplicate.Error!.FieldErrors![RoleFieldNames.Name]);

        // A role manager whose access comes from a custom role cannot remove Manage roles from it.
        var keeper = (await roles.CreateAsync(new SaveRoleRequest($"Keeper {Guid.NewGuid():N}"[..18], null,
            [Permissions.RolesManage, Permissions.CasesView]), CancellationToken.None)).Value!;
        var keeperUser = await CreateAsync(NewEmail("keeper"), keeper.Name);
        var keeperActor = FakeCurrentUser.Authenticated(keeperUser.Id, keeperUser.UserName, keeper.Name, [Permissions.RolesManage, Permissions.CasesView]);
        var lockout = await RolesAs(keeperActor).UpdateAsync(keeper.Id, new SaveRoleRequest(keeper.Name, null, [Permissions.CasesView], keeper.ConcurrencyStamp), CancellationToken.None);
        Assert.Contains("lose the ability to manage roles", lockout.Error!.FieldErrors![RoleFieldNames.Permissions].Single());

        // A role with members cannot be deleted; once empty it can.
        var withMember = await roles.DeleteAsync(keeper.Id, keeper.ConcurrencyStamp, CancellationToken.None);
        Assert.Equal(ErrorKind.Forbidden, withMember.Error!.Kind);
        var keeperDetail = (await UsersAs(AdministrationTestSupport.Administrator).GetAsync(keeperUser.Id, CancellationToken.None)).Value!;
        Assert.True((await UsersAs(AdministrationTestSupport.Administrator).UpdateAsync(keeperUser.Id,
            new UpdateUserRequest(keeperUser.UserName, [AppRoles.Auditor], keeperDetail.ConcurrencyStamp), CancellationToken.None)).IsSuccess);
        var current = (await roles.GetAsync(keeper.Id, CancellationToken.None)).Value!;
        Assert.Equal(keeper.Name, (await roles.DeleteAsync(keeper.Id, current.ConcurrencyStamp, CancellationToken.None)).Value);
    }

    [Test]
    public async Task Renaming_a_custom_role_keeps_its_members_and_signs_them_in_again()
    {
        var roles = RolesAs(AdministrationTestSupport.Administrator);
        var role = (await roles.CreateAsync(new SaveRoleRequest($"Old {Guid.NewGuid():N}"[..16], null, [Permissions.CasesView]), CancellationToken.None)).Value!;
        var member = await CreateAsync(NewEmail("member"), role.Name);
        var newName = $"New {Guid.NewGuid():N}"[..16];

        var renamed = await roles.UpdateAsync(role.Id, new SaveRoleRequest(newName, null, [Permissions.CasesView], role.ConcurrencyStamp), CancellationToken.None);

        Assert.True(renamed.IsSuccess, renamed.Error?.Message);
        Assert.Equal([member.UserName], renamed.Value!.Members);
        var after = (await UsersAs(AdministrationTestSupport.Administrator).GetAsync(member.Id, CancellationToken.None)).Value!;
        Assert.Equal([newName], after.Roles);
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.NotEqual(member.ConcurrencyStamp, (await users.FindByIdAsync(member.Id))!.ConcurrencyStamp);
    }

    [Test]
    public async Task The_user_list_filters_by_email_role_and_status_and_pages()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync($"page-{tag}-{i}@example.test", AppRoles.Auditor);
        }
        var inactive = await CreateAsync($"page-{tag}-z@example.test", AppRoles.QualityAnalyst);
        await UsersAs(AdministrationTestSupport.Administrator).SetStatusAsync(inactive.Id, new SetUserStatusRequest(false, inactive.ConcurrencyStamp), CancellationToken.None);
        var service = UsersAs(AdministrationTestSupport.Administrator);

        var first = (await service.ListAsync(new UserListQuery($"PAGE-{tag}", PageSize: 2), CancellationToken.None)).Value!;
        var second = (await service.ListAsync(new UserListQuery($"page-{tag}", Page: 2, PageSize: 2), CancellationToken.None)).Value!;
        var auditors = (await service.ListAsync(new UserListQuery($"page-{tag}", AppRoles.Auditor), CancellationToken.None)).Value!;
        var inactiveOnly = (await service.ListAsync(new UserListQuery($"page-{tag}", Status: UserStatusFilter.Inactive), CancellationToken.None)).Value!;

        Assert.Equal(4, first.TotalCount);
        Assert.Equal([$"page-{tag}-0@example.test", $"page-{tag}-1@example.test"], first.Items.Select(u => u.UserName));
        Assert.Equal([$"page-{tag}-2@example.test", $"page-{tag}-z@example.test"], second.Items.Select(u => u.UserName));
        Assert.Equal(3, auditors.TotalCount);
        Assert.False(Assert.Single(inactiveOnly.Items).IsActive);
    }

    [Test]
    public async Task A_locked_out_user_is_unlocked()
    {
        var user = await CreateAsync(NewEmail("locked"), AppRoles.Auditor);
        await using (var scope = Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var stored = (await users.FindByIdAsync(user.Id))!;
            await users.SetLockoutEndDateAsync(stored, Services.GetRequiredService<TimeProvider>().GetUtcNow().AddMinutes(5));
        }
        var locked = (await UsersAs(AdministrationTestSupport.Administrator).GetAsync(user.Id, CancellationToken.None)).Value!;
        Assert.True(locked.IsLockedOut(Services.GetRequiredService<TimeProvider>().GetUtcNow()));

        var unlocked = await UsersAs(AdministrationTestSupport.Administrator).UnlockAsync(user.Id, locked.ConcurrencyStamp, CancellationToken.None);

        Assert.True(unlocked.IsSuccess, unlocked.Error?.Message);
        Assert.False(unlocked.Value!.IsLockedOut(Services.GetRequiredService<TimeProvider>().GetUtcNow()));
    }

    [Test]
    public async Task Case_owners_follow_stored_permissions_including_overrides_and_status()
    {
        var auditor = await CreateAsync(NewEmail("owner"), AppRoles.Auditor);
        await using var db = fixture.CreateContext();
        Assert.False(await CaseAssigneeDirectory.IsAssignableAsync(db, auditor.UserName, CancellationToken.None));

        var allowed = (await UsersAs(AdministrationTestSupport.Administrator).SetOverridesAsync(auditor.Id, new SetPermissionOverridesRequest(
            new Dictionary<string, PermissionEffect> { [Permissions.CasesEdit] = PermissionEffect.Allow }, auditor.ConcurrencyStamp), CancellationToken.None)).Value!;
        Assert.True(await CaseAssigneeDirectory.IsAssignableAsync(db, auditor.UserName, CancellationToken.None));
        Assert.Contains(await CaseAssigneeDirectory.QueryAsync(db, CancellationToken.None), a => a.UserName == auditor.UserName && a.Role == AppRoles.Auditor);

        await UsersAs(AdministrationTestSupport.Administrator).SetStatusAsync(auditor.Id, new SetUserStatusRequest(false, allowed.ConcurrencyStamp), CancellationToken.None);
        Assert.False(await CaseAssigneeDirectory.IsAssignableAsync(db, auditor.UserName, CancellationToken.None));
    }
}
