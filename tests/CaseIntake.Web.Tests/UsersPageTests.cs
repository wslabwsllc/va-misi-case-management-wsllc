using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Web.Components.Pages.Admin;

namespace CaseIntake.Web.Tests;

/// <summary>A user list held in memory; <see cref="NextError"/> makes the next create fail with that error.</summary>
public sealed class FakeUserAdministrationService : IUserAdministrationService
{
    public List<UserSummaryDto> Users { get; } = [new("1", "supervisor@example.test", [AppRoles.Supervisor])];
    public List<CreateUserRequest> Requests { get; } = [];
    public Error? NextError { get; set; }

    public Task<Result<IReadOnlyList<UserSummaryDto>>> ListAsync(CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<UserSummaryDto>>.Success([.. Users]));

    public Task<Result<UserSummaryDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        Requests.Add(request);
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
}
