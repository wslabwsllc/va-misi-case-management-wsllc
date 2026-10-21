using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Web.Components.Pages.Admin;

namespace CaseIntake.Web.Tests;

public class UsersPageTests : WebTestContext
{
    public UsersPageTests()
    {
        SignInAs(AppRoles.Supervisor);
    }

    [Fact]
    public void Lists_users_with_roles_status_and_a_link_to_each()
    {
        UserAdmin.Users.Add(FakeUserAdministrationService.Detail("2", "old@example.test", [AppRoles.Auditor], isActive: false));
        var cut = Render<Users>();

        var rows = cut.FindAll("[data-testid=user-row]");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Inactive", rows[0].TextContent);
        Assert.Equal("admin/users/2", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Supervisor", rows[1].TextContent);
        Assert.Contains("Never", rows[1].TextContent);
        Assert.Equal("Last sign-in", cut.FindAll("[data-testid=users-table] th")[3].TextContent);
    }

    [Fact]
    public void Filters_are_applied_together_and_announced()
    {
        UserAdmin.Users.Add(FakeUserAdministrationService.Detail("2", "clerk@example.test", [AppRoles.IntakeClerk]));
        var cut = Render<Users>();

        cut.Find("#user-search").Input("clerk");
        cut.Find("#user-role-filter").Change(AppRoles.IntakeClerk);
        cut.Find("#user-status-filter").Change(nameof(UserStatusFilter.Active));
        cut.Find("[data-testid=user-filters]").Submit();

        Assert.Equal(new UserListQuery("clerk", AppRoles.IntakeClerk, UserStatusFilter.Active, 1, 25), UserAdmin.Queries[^1]);
        Assert.Single(cut.FindAll("[data-testid=user-row]"));
        Assert.Contains("Showing 1", Announcer.Message);
    }

    [Fact]
    public void Roles_are_checkboxes_in_a_labelled_group_and_roles_beyond_the_actor_are_disabled_with_a_reason()
    {
        var cut = Render<Users>();

        Assert.Equal("Roles * (required)", cut.Find("[data-testid=user-role-group] legend").TextContent.Trim().Replace("  ", " "));
        Assert.Equal(RolePermissionMap.Roles.Length, cut.FindAll("[data-testid=user-role-group] input[type=checkbox]").Count);
        var admin = cut.Find("#user-role-administrator");
        Assert.True(admin.HasAttribute("disabled"));
        Assert.Contains("permissions you don't have", cut.Find($"#{admin.GetAttribute("aria-describedby")}").TextContent);
        Assert.False(cut.Find("#user-role-intakeclerk").HasAttribute("disabled"));
        Assert.Equal("new-password", cut.Find("#user-password").GetAttribute("autocomplete"));
    }

    [Fact]
    public void Adding_a_user_with_two_roles_confirms_it_lists_them_and_clears_the_form()
    {
        var cut = Render<Users>();
        cut.Find("#user-email").Input("clerk@example.test");
        cut.Find("#user-role-intakeclerk").Change(true);
        cut.Find("#user-role-auditor").Change(true);
        cut.Find("#user-password").Input("Initial-Passw0rd!");

        cut.Find("[data-testid=add-user-form]").Submit();

        var request = Assert.IsType<CreateUserRequest>(UserAdmin.Requests.Single());
        Assert.Equal("clerk@example.test", request.Email);
        Assert.Equal([AppRoles.Auditor, AppRoles.IntakeClerk], request.Roles);   // in the order the roles are listed
        Assert.Contains("clerk@example.test was added as Auditor, Intake clerk.", cut.Find("[data-testid=user-added]").TextContent);
        Assert.Equal(2, cut.FindAll("[data-testid=user-row]").Count);
        Assert.Equal("", cut.Find("#user-email").GetAttribute("value") ?? "");
        Assert.False(cut.Find("#user-role-intakeclerk").HasAttribute("checked"));
    }

    [Fact]
    public void Service_field_errors_are_shown_on_their_fields_and_in_the_summary()
    {
        UserAdmin.NextError = new(ErrorKind.Validation, "Validation", "Some fields need attention.",
            new Dictionary<string, string[]>
            {
                [UserFieldNames.Email] = ["A user with this email address already exists."],
                [UserFieldNames.Roles] = ["Choose at least one role."],
            });
        var cut = Render<Users>();
        cut.Find("#user-email").Input("supervisor@example.test");

        cut.Find("[data-testid=add-user-form]").Submit();

        Assert.Equal("A user with this email address already exists.", cut.Find("#user-email-error").TextContent.Trim());
        Assert.Equal("Choose at least one role.", cut.Find("#user-role-error").TextContent.Trim());
        Assert.Contains("user-role-error", cut.Find("[data-testid=user-role-group]").GetAttribute("aria-describedby"));
        var links = cut.FindAll("#error-summary a");
        Assert.Equal(2, links.Count);
        Assert.Equal("#user-role-auditor", links[1].GetAttribute("href"));
        Assert.Single(UserAdmin.Users);
    }

    [Fact]
    public void A_form_level_failure_is_shown_in_the_alert_and_cleared_by_the_next_success()
    {
        UserAdmin.NextError = new(ErrorKind.Forbidden, "Forbidden", "You do not have permission to perform this action.");
        var cut = Render<Users>();
        cut.Find("[data-testid=add-user-form]").Submit();

        Assert.Contains("The user was not added", cut.Find("[data-testid=users-error]").TextContent);

        cut.Find("#user-email").Input("new@example.test");
        cut.Find("#user-role-auditor").Change(true);
        cut.Find("#user-password").Input("Initial-Passw0rd!");
        cut.Find("[data-testid=add-user-form]").Submit();

        Assert.Equal("", cut.Find("[data-testid=users-error]").TextContent.Trim());
    }

    [Fact]
    public void A_list_failure_or_exception_is_shown_and_the_table_is_empty()
    {
        UserAdmin.ListError = new(ErrorKind.Forbidden, "Forbidden", "You cannot list users.");
        var failed = Render<Users>();
        Assert.Contains("The user list could not be loaded", failed.Find("[data-testid=users-error]").TextContent);
        Assert.Contains("You cannot list users.", failed.Find("[data-testid=users-error]").TextContent);
        Assert.Empty(failed.FindAll("[data-testid=users-table] tbody tr"));

        UserAdmin.ListError = null;
        UserAdmin.ListException = new InvalidOperationException("db");
        var thrown = Render<Users>();
        Assert.Contains("Reload the page to try again.", thrown.Find("[data-testid=users-error]").TextContent);
        Assert.DoesNotContain("db", thrown.Find("[data-testid=users-error]").TextContent);
        Assert.Empty(thrown.FindAll("[data-testid=users-table] tbody tr"));
    }

    [Fact]
    public void An_unexpected_create_failure_shows_a_generic_message()
    {
        UserAdmin.NextException = new InvalidOperationException("smtp");
        var cut = Render<Users>();
        cut.Find("#user-email").Input("clerk@example.test");
        cut.Find("#user-role-auditor").Change(true);
        cut.Find("#user-password").Input("Initial-Passw0rd!");

        cut.Find("[data-testid=add-user-form]").Submit();

        var alert = cut.Find("[data-testid=users-error]").TextContent;
        Assert.Contains("The user was not added", alert);
        Assert.Contains("Something went wrong. Try again.", alert);
        Assert.DoesNotContain("smtp", cut.Markup);
        Assert.Single(UserAdmin.Users);
    }
}

public class UserDetailPageTests : WebTestContext
{
    public UserDetailPageTests()
    {
        SignInAs(AppRoles.Supervisor);
        UserAdmin.Users.Add(FakeUserAdministrationService.Detail("7", "worker@example.test", [AppRoles.CaseWorker],
            lockoutEndUtc: Time.GetUtcNow().AddMinutes(3)));
    }

    private IRenderedComponent<UserDetail> RenderUser(string id = "7") => Render<UserDetail>(p => p.Add(x => x.Id, id));

    [Fact]
    public void Shows_account_roles_and_every_permission_with_its_source_and_result()
    {
        var cut = RenderUser();

        Assert.Equal("worker@example.test", cut.Find("h1").TextContent.Trim());
        Assert.Contains("Locked out after failed sign-ins", cut.Find("[data-testid=user-lockout]").TextContent);
        Assert.True(cut.Find("#edit-role-caseworker").HasAttribute("checked"));
        Assert.Equal(PermissionCatalog.All.Length, cut.FindAll("[data-testid=permissions-table] tbody th[scope=row]").Count);
        Assert.Equal(PermissionCatalog.Groups.Length, cut.FindAll("[data-testid=permissions-table] th[scope=rowgroup]").Count);
        Assert.Contains("Case worker", cut.Find($"[data-testid='permission-{Permissions.CasesEdit}']").TextContent);
        Assert.Contains("Has it", cut.Find($"[data-testid='effective-{Permissions.CasesEdit}']").TextContent);
        Assert.Contains("Doesn't have it", cut.Find($"[data-testid='effective-{Permissions.CasesAssign}']").TextContent);
    }

    [Fact]
    public void Each_permission_override_is_a_labelled_radio_group_and_ones_the_actor_lacks_are_disabled()
    {
        var cut = RenderUser();

        var edit = cut.Find($"[data-testid='permission-{Permissions.CasesEdit}'] fieldset");
        Assert.Equal("Override for Edit cases", edit.QuerySelector("legend")!.TextContent.Trim());
        Assert.Equal(3, edit.QuerySelectorAll("input[type=radio]").Length);
        Assert.True(cut.Find($"[data-testid='override-{Permissions.CasesEdit}-inherit']").HasAttribute("checked"));
        Assert.True(cut.Find($"[data-testid='permission-{Permissions.TestResultsView}'] fieldset").HasAttribute("disabled"));
    }

    [Fact]
    public void Saving_overrides_sends_the_full_set_with_the_stamp_and_shows_the_result()
    {
        var cut = RenderUser();
        cut.Find($"[data-testid='override-{Permissions.CasesEdit}-deny']").Change(true);
        cut.Find($"[data-testid='override-{Permissions.CasesAssign}-allow']").Change(true);

        cut.Find("[data-testid=overrides-form]").Submit();

        var request = Assert.IsType<SetPermissionOverridesRequest>(UserAdmin.Requests.Single());
        Assert.Equal("stamp-0", request.ConcurrencyStamp);
        Assert.Equal(PermissionEffect.Deny, request.Overrides[Permissions.CasesEdit]);
        Assert.Equal(PermissionEffect.Allow, request.Overrides[Permissions.CasesAssign]);
        Assert.Contains("Doesn't have it", cut.Find($"[data-testid='effective-{Permissions.CasesEdit}']").TextContent);
        Assert.Contains("Has it", cut.Find($"[data-testid='effective-{Permissions.CasesAssign}']").TextContent);
        Assert.Equal("Permissions saved.", Announcer.Message);
    }

    [Fact]
    public void Saving_roles_sends_the_chosen_roles_and_a_conflict_reloads_and_explains()
    {
        var cut = RenderUser();
        cut.Find("#edit-role-supervisor").Change(true);
        UserAdmin.NextError = new(ErrorKind.Conflict, "Conflict", "Someone else changed this while you were editing.");

        cut.Find("[data-testid=user-profile-form]").Submit();

        var request = Assert.IsType<UpdateUserRequest>(UserAdmin.Requests.Single());
        Assert.Equal([AppRoles.CaseWorker, AppRoles.Supervisor], request.Roles);
        Assert.Contains("Someone else changed this", cut.Find("[data-testid=user-error]").TextContent);
        Assert.False(cut.Find("#edit-role-supervisor").HasAttribute("checked"));   // the latest version is shown
    }

    [Fact]
    public void Deactivating_asks_for_confirmation_in_a_dialog_then_shows_the_new_status()
    {
        var cut = RenderUser();
        cut.Find("[data-testid=deactivate-user]").Click();

        InteropModule.VerifyInvoke("showDialog");
        Assert.Contains("signed out within a minute", cut.Find("[data-testid=user-status-dialog]").TextContent);
        cut.Find("[data-testid=user-status-confirm]").Click();

        Assert.Equal(new SetUserStatusRequest(false, "stamp-0"), UserAdmin.Requests.Single());
        Assert.Equal("Inactive", cut.Find("[data-testid=user-status]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=reactivate-user]"));
        Assert.Equal("worker@example.test was deactivated.", Announcer.Message);
    }

    [Fact]
    public void A_user_the_actor_may_not_manage_is_read_only_with_the_reason()
    {
        UserAdmin.Users.Add(FakeUserAdministrationService.Detail("9", "admin@example.test", [AppRoles.Administrator],
            canManage: false, reason: "This person has permissions you do not have."));
        var cut = RenderUser("9");

        Assert.Contains("permissions you do not have", cut.Find("[data-testid=user-read-only]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=user-profile-form]"));
        Assert.Empty(cut.FindAll("[data-testid=deactivate-user]"));
        Assert.Empty(cut.FindAll("[data-testid=permissions-table] input"));
    }

    [Fact]
    public void An_unknown_user_shows_not_found()
    {
        var cut = RenderUser("missing");

        Assert.Contains("User not found", cut.Find("[data-testid=user-not-found]").TextContent);
    }
}

public class RolesPageTests : WebTestContext
{
    public RolesPageTests()
    {
        SignInAs(AppRoles.Administrator);
    }

    [Fact]
    public void Lists_every_role_with_type_members_and_permissions()
    {
        var cut = Render<Roles>();

        var rows = cut.FindAll("[data-testid=role-row]");
        Assert.Equal(RolePermissionMap.Roles.Length, rows.Count);
        Assert.Contains("Built in, fixed", rows[^1].TextContent);
        Assert.Contains($"All {PermissionCatalog.All.Length} permissions", rows[^1].TextContent);
        Assert.Contains("3: View cases, Download documents, View audit log", rows[0].TextContent);
    }

    [Fact]
    public void Creating_a_role_sends_the_checked_permissions_in_catalog_order_and_opens_it()
    {
        var cut = Render<RoleEditor>();
        cut.Find("#role-name").Input("Records reviewer");
        cut.Find("#perm-attachments-download").Change(true);
        cut.Find("#perm-cases-view").Change(true);

        cut.Find("[data-testid=role-form]").Submit();

        var request = Assert.IsType<SaveRoleRequest>(RoleAdmin.Requests.Single());
        Assert.Equal("Records reviewer", request.Name);
        Assert.Equal([Permissions.CasesView, Permissions.AttachmentsDownload], request.Permissions);
        Assert.EndsWith("admin/roles/custom-6", Navigation.Uri);
        Assert.Equal("The Records reviewer role was created.", Announcer.Message);
    }

    [Fact]
    public void A_built_in_role_keeps_its_name_and_permissions_are_grouped_in_labelled_fieldsets()
    {
        var cut = Render<RoleEditor>(p => p.Add(x => x.Id, "role-caseworker"));

        Assert.Empty(cut.FindAll("#role-name"));
        Assert.Contains("built-in roles keep their names", cut.Find("[data-testid=role-name-fixed]").TextContent);
        Assert.Equal(PermissionCatalog.Groups, cut.FindAll(".permission-group legend").Select(l => l.TextContent.Trim()));
        Assert.True(cut.Find("#perm-cases-edit").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("[data-testid=delete-role]"));
    }

    [Fact]
    public void The_administrator_role_is_read_only_with_the_reason()
    {
        var cut = Render<RoleEditor>(p => p.Add(x => x.Id, "role-administrator"));

        Assert.Contains("always has every permission", cut.Find("[data-testid=role-read-only]").TextContent);
        Assert.All(cut.FindAll("[data-testid=role-permissions] input"), i => Assert.True(i.HasAttribute("disabled")));
        Assert.Empty(cut.FindAll("[data-testid=save-role]"));
    }

    [Fact]
    public void Permissions_the_actor_lacks_cannot_be_changed_and_say_why()
    {
        SignInAs(AppRoles.Supervisor);
        var cut = Render<RoleEditor>();

        Assert.True(cut.Find("#perm-testresults-view").HasAttribute("disabled"));
        Assert.Contains("You don't hold this permission", cut.Find("#perm-testresults-view-description").TextContent);
        Assert.False(cut.Find("#perm-cases-view").HasAttribute("disabled"));
    }

    [Fact]
    public void A_custom_role_with_members_cannot_be_deleted_and_an_empty_one_is_deleted_after_confirming()
    {
        RoleAdmin.Roles.Add(new RoleDetailDto("busy", "Busy", null, false, false, [Permissions.CasesView], ["a@example.test"], "s", true, null));
        RoleAdmin.Roles.Add(new RoleDetailDto("empty", "Empty", null, false, false, [Permissions.CasesView], [], "s", true, null));

        var busy = Render<RoleEditor>(p => p.Add(x => x.Id, "busy"));
        Assert.True(busy.Find("[data-testid=delete-role]").HasAttribute("disabled"));
        Assert.Contains("Remove everyone", busy.Find("#delete-role-hint").TextContent);

        var empty = Render<RoleEditor>(p => p.Add(x => x.Id, "empty"));
        empty.Find("[data-testid=delete-role]").Click();
        empty.Find("[data-testid=delete-role-confirm]").Click();

        Assert.Equal("empty", RoleAdmin.Requests.Single());
        Assert.EndsWith("admin/roles", Navigation.Uri);
        Assert.Equal("The Empty role was deleted.", Announcer.Message);
    }
}
