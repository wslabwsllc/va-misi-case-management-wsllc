using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>A supervisor adds a user in the browser; the new user signs in, changes the initial password and signs in with the new one.</summary>
public sealed class UserAdministrationTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    private const string InitialPassword = "Initial-Passw0rd!";
    private const string NewPassword = "Changed-Passw0rd!";

    [Fact]
    public async Task Supervisor_adds_a_user_who_signs_in_and_changes_their_password()
    {
        await using var s = await StartAsync(Meta("Supervisor adds a user; the user changes their password", "User administration", ["R-4.2-11"],
            expected: "The new intake clerk is listed, signs in with the initial password, changes it, and signs in with the new one"));
        var page = s.Page;
        var email = $"clerk-{Guid.NewGuid():N}@example.test";

        await SignInAsync(s, DemoAccounts.Supervisor);
        await s.StepAsync("Open Users from the side navigation", async () =>
        {
            await PressAsync(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "Users" }));
            await ExpectPageAsync(page, "Users");
        });
        await s.StepAsync("Add an intake clerk", async () =>
        {
            await page.Locator("#user-email").FillAsync(email);
            await page.Locator("#user-role").SelectOptionAsync("IntakeClerk");
            await page.Locator("#user-password").FillAsync(InitialPassword);
            await PressAsync(page.GetByTestId("add-user"));
            await Expect(page.GetByTestId("user-added")).ToContainTextAsync($"{email} was added as Intake clerk.");
            await Expect(page.GetByTestId("users-table")).ToContainTextAsync(email);
        });
        await s.AxeAsync("Users");
        await SignOutAsync(s);

        await SignInAsync(s, email, password: InitialPassword);
        await s.StepAsync("Change the initial password", async () =>
        {
            await Expect(page.GetByTestId("user-roles")).ToHaveTextAsync("Intake clerk");
            await PressAsync(page.GetByTestId("change-password-link"));
            await Expect(page.Locator("h1")).ToHaveTextAsync("Change password");
            await page.Locator("#current-password").FillAsync(InitialPassword);
            await page.Locator("#new-password").FillAsync(NewPassword);
            await page.Locator("#confirm-password").FillAsync(NewPassword);
            await PressAsync(page.GetByTestId("change-password"));
            await Expect(page.GetByTestId("password-changed")).ToContainTextAsync("Your password was changed.");
        });
        await s.AxeAsync("Change password");
        await GotoAsync(page, "/");
        await SignOutAsync(s);
        await SignInAsync(s, email, password: NewPassword);
        s.Complete();
    }
}
