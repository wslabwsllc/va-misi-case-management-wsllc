using System.Runtime.CompilerServices;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E.Infrastructure;

/// <summary>
/// Shared steps for the browser tests. Interaction is keyboard-first: controls are reached with Tab or by moving focus
/// to them, and activated with Enter or Space. File selection uses Playwright's programmatic file chooser
/// (SetInputFiles), which bypasses the native picker; the native picker and download dialogs are checked manually
/// (docs/accessibility-review.md).
/// </summary>
[Collection(E2ECollection.Name)]
public abstract class E2ETestBase(PlaywrightFixture fixture)
{
    protected PlaywrightFixture Fixture { get; } = fixture;

    protected TestMeta Meta(string title, string story, string[] requirements, string? description = null, string? preconditions = null,
        string? expected = null, string priority = "High", [CallerMemberName] string method = "") =>
        new($"{GetType().FullName}.{method}", title, story, requirements, priority, description, preconditions, expected);

    protected Task<E2ESession> StartAsync(TestMeta meta, ViewportSize? viewport = null, bool mobile = false) =>
        E2ESession.StartAsync(Fixture, meta, viewport, mobile);

    /// <summary>Signs in on the static sign-in page using only the keyboard and waits for the dashboard.</summary>
    protected static async Task SignInAsync(E2ESession s, string email, string? expectHeading = "Dashboard")
    {
        await s.StepAsync($"Sign in as {email} (keyboard)", async () =>
        {
            await s.Page.GotoAsync("/account/login");
            await Expect(s.Page).ToHaveTitleAsync("Sign in - Case Intake and Triage");
            await s.Page.Locator("#login-email").FocusAsync();
            await s.Page.Keyboard.TypeAsync(email);
            await s.Page.Keyboard.PressAsync("Tab");
            await E2ESession.ExpectFocusedAsync(s.Page.Locator("#login-password"));
            await s.Page.Keyboard.TypeAsync(E2ESettings.DemoPassword);
            await s.Page.Keyboard.PressAsync("Enter");
            if (expectHeading is not null)
            {
                await Expect(s.Page.Locator("h1")).ToHaveTextAsync(expectHeading);
            }
            await WaitForCircuitAsync(s.Page);
        }, expected: $"Signed in as {email}");
    }

    /// <summary>Waits until the Blazor circuit is connected (an interactive-only control exists).</summary>
    protected static async Task WaitForCircuitAsync(IPage page) =>
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined && (document.querySelector('[data-testid=nav-toggle]') !== null || document.querySelector('[data-testid=bottom-nav-more]') !== null)");

    /// <summary>Full page load of an app page, then wait for the circuit, so input is not lost to the interactive re-render.</summary>
    protected static async Task GotoAsync(IPage page, string url)
    {
        await page.GotoAsync(url);
        await WaitForCircuitAsync(page);
    }

    /// <summary>Moves keyboard focus to a control and activates it with Enter (links, buttons) or Space (checkboxes).</summary>
    protected static async Task PressAsync(ILocator control, string key = "Enter")
    {
        await control.FocusAsync();
        await E2ESession.ExpectFocusedAsync(control);
        await control.PressAsync(key);
    }

    /// <summary>After in-app navigation the new page's h1 has focus and the document title is updated.</summary>
    protected static async Task ExpectPageAsync(IPage page, string heading, string? documentTitle = null)
    {
        var h1 = page.Locator("h1");
        await Expect(h1).ToHaveTextAsync(heading);
        await E2ESession.ExpectFocusedAsync(h1);
        await Expect(page).ToHaveTitleAsync(documentTitle ?? $"{heading} - Case Intake and Triage");
    }

    protected static ILocator Announcer(IPage page) => page.GetByTestId("status-announcer");

    protected static async Task SignOutAsync(E2ESession s)
    {
        await s.StepAsync("Sign out (keyboard)", async () =>
        {
            var signOut = s.Page.GetByTestId("sign-out");
            await PressAsync(signOut);
            await Expect(s.Page.GetByTestId("signed-out")).ToHaveTextAsync("You have signed out.");
            await Expect(s.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/account/login"));
        }, expected: "Back on the sign-in page with 'You have signed out.'");
    }
}
