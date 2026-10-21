using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// Change 1 (ADR 0013), keyboard only: an administrator creates a role, gives it to a new user, overrides one of the
/// user's permissions with the radio group's arrow keys, and deactivates the user through the confirmation dialog, which
/// returns focus on Escape. Every page is scanned with axe.
/// </summary>
public sealed class RolesAndPermissionsTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    private const string Password = "Initial-Passw0rd!";

    [Fact]
    public async Task Administrator_manages_a_role_a_user_and_their_permissions_with_the_keyboard_only()
    {
        await using var s = await StartAsync(Meta("Administrator manages roles, users and permissions (keyboard only)", "User and role administration",
            ["R-4.2-11", "CR-1"],
            expected: "The custom role is created, the user gets it, a Deny override removes one permission, and the deactivated user cannot sign in"));
        var page = s.Page;
        var tag = Guid.NewGuid().ToString("N")[..6];
        var roleName = $"Reviewer {tag}";
        var roleInputId = $"user-role-reviewer-{tag}";
        var email = $"reviewer-{tag}@example.test";

        await SignInAsync(s, DemoAccounts.Administrator);
        await s.StepAsync("Open Roles from the side navigation", async () =>
        {
            await PressAsync(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "Roles" }));
            await ExpectPageAsync(page, "Roles");
            await Expect(page.GetByTestId("roles-table")).ToContainTextAsync("Built in, fixed");
        });
        await s.AxeAsync("Roles");

        await s.StepAsync("Create a role with two permissions", async () =>
        {
            await PressAsync(page.GetByTestId("new-role"));
            await ExpectPageAsync(page, "New role");
            await page.Locator("#role-name").FocusAsync();
            await page.Keyboard.TypeAsync(roleName);
            await page.Keyboard.PressAsync("Tab");
            await E2ESession.ExpectFocusedAsync(page.Locator("#role-description"));
            await page.Keyboard.TypeAsync("Reads cases and documents.");
            await PressAsync(page.Locator("#perm-cases-view"), "Space");
            await PressAsync(page.Locator("#perm-attachments-download"), "Space");
            await Expect(page.Locator("#perm-cases-view")).ToBeCheckedAsync();
            await PressAsync(page.GetByTestId("save-role"));
            await ExpectPageAsync(page, $"Role: {roleName}");
            await Expect(Announcer(page)).ToContainTextAsync($"The {roleName} role was created.");
        });
        await s.AxeAsync("Role editor");

        await s.StepAsync("Add a user with the new role", async () =>
        {
            await PressAsync(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "Users" }));
            await ExpectPageAsync(page, "Users");
            await page.Locator("#user-email").FocusAsync();
            await page.Keyboard.TypeAsync(email);
            await PressAsync(page.Locator($"#{roleInputId}"), "Space");
            await page.Locator("#user-password").FocusAsync();
            await page.Keyboard.TypeAsync(Password);
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByTestId("user-added")).ToContainTextAsync($"{email} was added as {roleName}.");
        });

        await s.StepAsync("Find the user and open their page", async () =>
        {
            await page.Locator("#user-search").FocusAsync();
            await page.Keyboard.TypeAsync(email);
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByTestId("user-row")).ToHaveCountAsync(1);
            await PressAsync(page.GetByTestId("user-row").GetByRole(AriaRole.Link, new() { Name = email }));
            await ExpectPageAsync(page, email, $"User {email} - Case Intake and Triage");
        });
        await s.AxeAsync("User detail");

        await s.StepAsync("Deny one permission with the arrow keys and save", async () =>
        {
            var inherit = page.GetByTestId("override-attachments.download-inherit");
            await inherit.FocusAsync();
            await page.Keyboard.PressAsync("ArrowRight");   // Allow
            await page.Keyboard.PressAsync("ArrowRight");   // Deny
            await Expect(page.GetByTestId("override-attachments.download-deny")).ToBeCheckedAsync();
            await PressAsync(page.GetByTestId("save-overrides"));
            await Expect(page.GetByTestId("user-saved")).ToContainTextAsync("Permissions saved.");
            await Expect(page.GetByTestId("effective-attachments.download")).ToContainTextAsync("Doesn't have it");
            await Expect(page.GetByTestId("effective-cases.view")).ToContainTextAsync("Has it");
        });

        await s.StepAsync("Deactivate the user: Escape cancels and returns focus, then confirm", async () =>
        {
            var trigger = page.GetByTestId("deactivate-user");
            var dialog = page.GetByTestId("user-status-dialog");
            await PressAsync(trigger);
            await Expect(dialog).ToBeVisibleAsync();
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("user-status-cancel"));
            await s.AxeAsync("Deactivate confirmation dialog");
            await page.Keyboard.PressAsync("Escape");
            await Expect(dialog).ToBeHiddenAsync();
            await E2ESession.ExpectFocusedAsync(trigger);
            await PressAsync(trigger);
            await PressAsync(page.GetByTestId("user-status-confirm"));
            await Expect(page.GetByTestId("user-status")).ToHaveTextAsync("Inactive");
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("reactivate-user"));
        });
        await SignOutAsync(s);

        await s.StepAsync("The deactivated user cannot sign in", async () =>
        {
            await page.GotoAsync("/account/login");
            await page.Locator("#login-email").FocusAsync();
            await page.Keyboard.TypeAsync(email);
            await page.Keyboard.PressAsync("Tab");
            await page.Keyboard.TypeAsync(Password);
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.Locator("#login-error")).ToContainTextAsync("Sign-in failed.");
            await E2ESession.ExpectFocusedAsync(page.Locator("#login-error"));
        });
        s.Complete();
    }

    [Fact]
    public async Task The_administration_pages_fit_a_phone_without_horizontal_scrolling()
    {
        await using var s = await StartAsync(Meta("Administration pages at 390x844", "User and role administration", ["R-4.2-12", "CR-1"],
            expected: "Users, a user's detail, Roles and the role editor fit the width; wide tables scroll inside their card"),
            Viewports.Phone390, mobile: true);
        var page = s.Page;

        await SignInAsync(s, DemoAccounts.Administrator);
        foreach (var (url, name) in new[]
        {
            ("/admin/users", "Users"),
            ("/admin/users/6f1d1c52-0e3a-4c1b-9a51-000000000001", "User detail"),
            ("/admin/roles", "Roles"),
            ("/admin/roles/new", "Role editor"),
            ("/admin/audit", "Audit log"),
        })
        {
            await s.StepAsync($"{name} fits the phone width", async () =>
            {
                await GotoAsync(page, url);
                await Expect(page.Locator("h1")).Not.ToHaveTextAsync("Access denied");
                var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
                Assert.True(overflow <= 0, $"{name}: the page is {overflow}px wider than the viewport");
            });
            await s.AxeAsync($"{name} (phone)");
        }
        s.Complete();
    }

    [Fact]
    public async Task A_supervisor_sees_the_administrator_as_read_only_and_cannot_open_roles()
    {
        await using var s = await StartAsync(Meta("Supervisor cannot manage the administrator or roles", "User and role administration", ["CR-1"],
            expected: "The administrator's page says why it is read-only; Roles is not in the navigation and its URL is denied"));
        var page = s.Page;

        await SignInAsync(s, DemoAccounts.Supervisor);
        await s.StepAsync("Open the administrator's page", async () =>
        {
            await GotoAsync(page, "/admin/users");
            await page.Locator("#user-search").FocusAsync();
            await page.Keyboard.TypeAsync(DemoAccounts.Administrator);
            await page.Keyboard.PressAsync("Enter");
            await PressAsync(page.GetByTestId("user-row").GetByRole(AriaRole.Link, new() { Name = DemoAccounts.Administrator }));
            await Expect(page.GetByTestId("user-read-only")).ToContainTextAsync("permissions you do not have");
            await Expect(page.GetByTestId("deactivate-user")).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "Roles" })).ToHaveCountAsync(0);
        });
        await s.AxeAsync("User detail (read-only)");
        await s.StepAsync("The Roles URL is denied", async () =>
        {
            await page.GotoAsync("/admin/roles");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Access denied");
        });
        s.Complete();
    }
}
