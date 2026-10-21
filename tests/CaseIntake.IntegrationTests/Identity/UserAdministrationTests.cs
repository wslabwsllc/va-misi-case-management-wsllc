using System.Net;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Administration;
using CaseIntake.Persistence;
using CaseIntake.TestKit.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>Supervisors add users of the roles within their access, and users change their own password, against real Identity and cookies.</summary>
public class UserAdministrationTests : ClassFixture<UserAdministrationTests, IdentityWebFixture>
{
    private static IdentityWebFixture fixture => Shared;

    private const string InitialPassword = "Initial-Passw0rd!";
    private const string NewPassword = "Changed-Passw0rd!";

    private UserAdministrationService Service(string role) => AdministrationTestSupport.Users(fixture.Factory.Services, FakeCurrentUser.ForRole(role));

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    [TestCaseSource(nameof(Roles))]
    public async Task A_supervisor_adds_a_user_of_each_role_they_may_grant_who_can_then_sign_in_with_that_role(string role)
    {
        var email = NewEmail("new");

        var result = await Service(AppRoles.Supervisor).CreateAsync(new CreateUserRequest($" {email} ", [role], InitialPassword), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(email, result.Value!.UserName);
        var listed = (await Service(AppRoles.Supervisor).ListAsync(new UserListQuery(Search: email), CancellationToken.None)).Value!;
        Assert.Equal([role], listed.Items.Single().Roles);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3011 && Equals(r.GetStructuredStateValue("UserId"), result.Value.Id));

        using var session = fixture.NewSession();
        await session.SignInAsync(email, InitialPassword);
        var html = await session.Client.GetStringAsync("/");
        Assert.Contains($"data-testid=\"user-roles\">{AppRoles.DisplayName(role)}<", html);
    }

    /// <summary>The roles whose permissions a supervisor holds (not Quality analyst or Administrator; see RolesAndPermissionsTests).</summary>
    public static TheoryData<string> Roles => [AppRoles.Auditor, AppRoles.IntakeClerk, AppRoles.CaseWorker, AppRoles.Supervisor];

    [TestCase(AppRoles.Auditor)]
    [TestCase(AppRoles.IntakeClerk)]
    [TestCase(AppRoles.CaseWorker)]
    [TestCase(AppRoles.QualityAnalyst)]
    public async Task Other_roles_cannot_list_or_add_users(string role)
    {
        var email = NewEmail("denied");

        var created = await Service(role).CreateAsync(new CreateUserRequest(email, [AppRoles.Auditor], InitialPassword), CancellationToken.None);
        var listed = await Service(role).ListAsync(new UserListQuery(), CancellationToken.None);

        Assert.Equal(ErrorKind.Forbidden, created.Error?.Kind);
        Assert.Equal(ErrorKind.Forbidden, listed.Error?.Kind);
        Assert.Null(await FindAsync(email));
    }

    [Test]
    public async Task Invalid_input_returns_field_errors_and_creates_nothing()
    {
        var email = NewEmail("weak");
        var service = Service(AppRoles.Supervisor);

        var badRole = await service.CreateAsync(new CreateUserRequest("not-an-email", ["Admin"], ""), CancellationToken.None);
        var weak = await service.CreateAsync(new CreateUserRequest(email, [AppRoles.CaseWorker], "short"), CancellationToken.None);
        var duplicate = await service.CreateAsync(new CreateUserRequest("SUPERVISOR@example.test", [AppRoles.CaseWorker], InitialPassword), CancellationToken.None);

        Assert.Equal([UserFieldNames.Email, UserFieldNames.Password, UserFieldNames.Roles], badRole.Error!.FieldErrors!.Keys.Order());
        Assert.Equal([UserFieldNames.Password], weak.Error!.FieldErrors!.Keys);
        Assert.Contains(weak.Error.FieldErrors[UserFieldNames.Password], m => m.Contains("at least 6", StringComparison.Ordinal));
        Assert.Equal(["A user with this email address already exists."], duplicate.Error!.FieldErrors![UserFieldNames.Email]);
        Assert.Null(await FindAsync(email));
    }

    [Test]
    public async Task The_users_page_is_for_supervisors_only()
    {
        using var supervisor = fixture.NewSession();
        await supervisor.SignInAsync("supervisor@example.test");
        using var caseWorker = fixture.NewSession();
        await caseWorker.SignInAsync("caseworker@example.test");

        Assert.Equal(HttpStatusCode.OK, (await supervisor.Client.GetAsync("/admin/users")).StatusCode);
        using var denied = await caseWorker.Client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("http://localhost/account/access-denied", denied.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Test]
    public async Task A_user_changes_their_password_the_old_one_stops_working_and_other_sessions_end()
    {
        var email = NewEmail("change");
        Assert.True((await Service(AppRoles.Supervisor).CreateAsync(new CreateUserRequest(email, [AppRoles.CaseWorker], InitialPassword), CancellationToken.None)).IsSuccess);
        using var other = fixture.NewSession();
        await other.SignInAsync(email, InitialPassword);
        using var session = fixture.NewSession();
        await session.SignInAsync(email, InitialPassword);

        var wrong = await PostChangeAsync(session, "Wrong-Passw0rd!", NewPassword);
        Assert.Contains("Your current password is not correct.", wrong);
        var changed = await PostChangeAsync(session, InitialPassword, NewPassword);
        Assert.Contains("data-testid=\"password-changed\"", changed);
        Assert.DoesNotContain(NewPassword, changed, StringComparison.Ordinal);

        // This browser's cookie was refreshed; the other session's stamp is stale and ends at the next revalidation (1 s here).
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/")).StatusCode);
        var status = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 60 && status == HttpStatusCode.OK; attempt++)
        {
            await Task.Delay(250);
            using var response = await other.Client.GetAsync("/");
            status = response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.Redirect, status);

        using var fresh = fixture.NewSession();
        using var oldLogin = await fresh.LoginAsync(email, InitialPassword);
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);   // the form again, with the generic error
        await fresh.SignInAsync(email, NewPassword);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3012);
    }

    [Test]
    public async Task Mismatched_new_passwords_are_rejected_without_changing_anything()
    {
        using var session = fixture.NewSession();
        await session.SignInAsync(IdentityWebFixture.LogoutUser);

        var html = await PostChangeAsync(session, IdentityTestSupport.DemoPassword, NewPassword, confirm: "Something-Else1!");

        Assert.Contains("The new passwords do not match.", html);
        using var fresh = fixture.NewSession();
        await fresh.SignInAsync(IdentityWebFixture.LogoutUser, IdentityTestSupport.DemoPassword);
    }

    [Test]
    public async Task The_change_password_page_requires_sign_in()
    {
        using var session = fixture.NewSession();

        using var response = await session.Client.GetAsync("/account/change-password");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/account/login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    private static async Task<string> PostChangeAsync(BrowserSession session, string current, string next, string? confirm = null)
    {
        var token = IdentityTestSupport.AntiforgeryToken(await session.Client.GetStringAsync("/account/change-password"));
        Assert.NotNull(token);
        using var response = await session.Client.PostAsync("/account/change-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "change-password",
            ["__RequestVerificationToken"] = token!,
            ["Input.CurrentPassword"] = current,
            ["Input.NewPassword"] = next,
            ["Input.ConfirmPassword"] = confirm ?? next,
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<AppUser?> FindAsync(string email)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync(email);
    }
}
