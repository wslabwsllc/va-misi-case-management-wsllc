using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Web.Components.Pages.Admin;

namespace CaseIntake.Web.Tests;

/// <summary>
/// A user list held in memory; <see cref="NextError"/> makes the next create fail with that error, and the list can be
/// made to fail or throw.
/// </summary>
public sealed class FakeUserAdministrationService : IUserAdministrationService
{
    public List<UserSummaryDto> Users { get; } = [new("1", "supervisor@example.test", [AppRoles.Supervisor])];
    public List<CreateUserRequest> Requests { get; } = [];
    public Error? NextError { get; set; }
    public Exception? NextException { get; set; }
    public Error? ListError { get; set; }
    public Exception? ListException { get; set; }

    public Task<Result<IReadOnlyList<UserSummaryDto>>> ListAsync(CancellationToken ct)
    {
        if (ListException is { } ex)
        {
            throw ex;
        }
        return Task.FromResult(ListError is { } error
            ? Result<IReadOnlyList<UserSummaryDto>>.Failure(error)
            : Result<IReadOnlyList<UserSummaryDto>>.Success([.. Users]));
    }

    public Task<Result<UserSummaryDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (NextException is { } ex)
        {
            NextException = null;
            throw ex;
        }
        if (NextError is { } error)
        {
            NextError = null;
            return Task.FromResult(Result<UserSummaryDto>.Failure(error));
        }
        var user = new UserSummaryDto($"{Users.Count + 1}", request.Email, [request.Role]);
        Users.Add(user);
        return Task.FromResult(Result<UserSummaryDto>.Success(user));
    }
}

public class UsersPageTests : WebTestContext
{
    public UsersPageTests()
    {
        SignInAs(AppRoles.Supervisor);
    }

    [Fact]
    public void Lists_users_and_offers_every_role()
    {
        var cut = Render<Users>();

        Assert.Contains("supervisor@example.test", cut.Find("[data-testid=users-table]").TextContent);
        var roles = cut.FindAll("#user-role option").Select(o => o.GetAttribute("value")).Where(v => v is { Length: > 0 });
        Assert.Equal(RolePermissionMap.Roles, roles);
        Assert.Equal("new-password", cut.Find("#user-password").GetAttribute("autocomplete"));
    }

    [Fact]
    public void Adding_a_user_confirms_it_lists_them_and_clears_the_form()
    {
        var cut = Render<Users>();
        cut.Find("#user-email").Input("clerk@example.test");
        cut.Find("#user-role").Change(AppRoles.IntakeClerk);
        cut.Find("#user-password").Input("Initial-Passw0rd!");

        cut.Find("[data-testid=add-user-form]").Submit();

        Assert.Equal(new CreateUserRequest("clerk@example.test", AppRoles.IntakeClerk, "Initial-Passw0rd!"), UserAdmin.Requests.Single());
        Assert.Contains("clerk@example.test was added as Intake clerk.", cut.Find("[data-testid=user-added]").TextContent);
        Assert.Contains("clerk@example.test", cut.Find("[data-testid=users-table]").TextContent);
        Assert.Equal("", cut.Find("#user-email").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Service_field_errors_are_shown_on_their_fields_and_in_the_summary()
    {
        UserAdmin.NextError = new(ErrorKind.Validation, "Validation", "Some fields need attention.",
            new Dictionary<string, string[]>
            {
                [UserFieldNames.Email] = ["A user with this email address already exists."],
                [UserFieldNames.Role] = ["Choose a role."],
            });
        var cut = Render<Users>();
        cut.Find("#user-email").Input("supervisor@example.test");

        cut.Find("[data-testid=add-user-form]").Submit();

        Assert.Equal("A user with this email address already exists.", cut.Find("#user-email-error").TextContent.Trim());
        Assert.Equal("Choose a role.", cut.Find("#user-role-error").TextContent.Trim());
        Assert.Equal("true", cut.Find("#user-role").GetAttribute("aria-invalid"));
        Assert.Equal(2, cut.FindAll("#error-summary a").Count);
        Assert.Single(UserAdmin.Users);
    }

    [Fact]
    public void A_list_failure_or_exception_is_shown_and_the_table_is_empty()
    {
        UserAdmin.ListError = new(ErrorKind.Forbidden, "Forbidden", "You cannot list users.");
        var failed = Render<Users>();
        Assert.Contains("You cannot list users.", failed.Find("[data-testid=users-error]").TextContent);
        Assert.Empty(failed.FindAll("[data-testid=users-table] tbody tr"));

        UserAdmin.ListError = null;
        UserAdmin.ListException = new InvalidOperationException("db");
        var thrown = Render<Users>();
        Assert.Contains("The user list could not be loaded. Reload the page to try again.", thrown.Find("[data-testid=users-error]").TextContent);
        Assert.Empty(thrown.FindAll("[data-testid=users-table] tbody tr"));
    }

    [Fact]
    public void A_create_error_without_field_errors_is_shown_in_the_alert_and_focused()
    {
        UserAdmin.NextError = new(ErrorKind.Forbidden, "Forbidden", "Only supervisors can add users.");
        var cut = Render<Users>();
        cut.Find("#user-email").Input("clerk@example.test");
        cut.Find("#user-password").Input("Initial-Passw0rd!");

        cut.Find("[data-testid=add-user-form]").Submit();

        var alert = cut.Find("[data-testid=users-error]");
        Assert.Contains("The user was not added", alert.TextContent);
        Assert.Contains("Only supervisors can add users.", alert.TextContent);
        JSInterop.VerifyFocusAsyncInvoke();
        // The password is never kept after a failed attempt; the email is.
        Assert.Equal("", cut.Find("#user-password").GetAttribute("value") ?? "");
        Assert.Equal("clerk@example.test", cut.Find("#user-email").GetAttribute("value"));
        Assert.Empty(cut.Find("[data-testid=user-added]").TextContent.Trim());
    }

    [Fact]
    public void An_unexpected_create_failure_shows_a_generic_message()
    {
        UserAdmin.NextException = new InvalidOperationException("smtp");
        var cut = Render<Users>();
        cut.Find("#user-email").Input("clerk@example.test");

        cut.Find("[data-testid=add-user-form]").Submit();

        Assert.Contains("Something went wrong and the user was not added. Try again.", cut.Find("[data-testid=users-error]").TextContent);
        Assert.DoesNotContain("smtp", cut.Markup);
        Assert.Single(UserAdmin.Users);
    }
}
