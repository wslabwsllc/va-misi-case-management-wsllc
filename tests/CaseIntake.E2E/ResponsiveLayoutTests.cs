using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// The same deployment at phone (390x844, 375x812), tablet (768x1024) and desktop (1440x900) sizes: the right shell
/// (sidebar or bottom bar), KPI cards and charts, case cards or table, forms, sheets, touch target size, no horizontal
/// overflow, no JavaScript errors, and an axe scan per layout.
/// </summary>
public sealed class ResponsiveLayoutTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public Task Phone_390x844_uses_the_mobile_app_shell() => PhoneAsync(Viewports.Phone390);

    [Fact]
    public Task Phone_375x812_uses_the_mobile_app_shell() => PhoneAsync(Viewports.Phone375);

    [Fact]
    public Task Tablet_768x1024_uses_the_collapsible_sidebar() => WideAsync(Viewports.Tablet768);

    [Fact]
    public Task Desktop_1440x900_uses_the_full_enterprise_layout() => WideAsync(Viewports.Desktop1440);

    private static async Task ExpectNoHorizontalOverflowAsync(IPage page, string where)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"{where}: the page is {overflow}px wider than the viewport");
    }

    private async Task PhoneAsync(ViewportSize viewport, [System.Runtime.CompilerServices.CallerMemberName] string method = "")
    {
        await using var s = await StartAsync(Meta($"Mobile shell at {Viewports.Label(viewport)}", "Mobile experience", ["R-4.2-12"],
            expected: "Bottom navigation, app bar, cards, sheet, FAB for create permission, no overflow, 44px targets", method: method),
            viewport, mobile: true);
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.CaseWorker);
        await s.StepAsync("Phone shell: app bar and bottom navigation shown, desktop sidebar hidden", async () =>
        {
            await Expect(page.GetByTestId("mobile-header")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("bottom-nav")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("side-nav")).ToBeHiddenAsync();
            var items = page.GetByTestId("bottom-nav").Locator(".bottom-nav__item");
            await Expect(items).ToHaveCountAsync(5);
            await Expect(page.GetByTestId("bottom-nav")).ToContainTextAsync("Home");
            await Expect(page.GetByTestId("bottom-nav")).ToContainTextAsync("More");
            foreach (var item in await items.AllAsync())
            {
                var box = await item.BoundingBoxAsync();
                Assert.True(box is { Width: >= 44, Height: >= 44 }, $"Bottom navigation target is {box?.Width}x{box?.Height}px");
            }
        });
        await s.StepAsync("Dashboard: headline KPI cards in two columns, the case trend chart, no overflow", async () =>
        {
            await Expect(page.GetByTestId("kpi-total")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("kpi-overdue")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("kpi-resolved")).ToBeHiddenAsync();
            await Expect(page.GetByTestId("chart-volume").Locator("svg.chart__svg")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("chart-status")).ToBeHiddenAsync();
            await Expect(page.GetByTestId("fab-new-case")).ToBeVisibleAsync();
            await ExpectNoHorizontalOverflowAsync(page, "dashboard");
        });
        await s.ScreenshotAsync($"Phone dashboard {Viewports.Label(viewport)}");
        await s.AxeAsync($"Phone dashboard {Viewports.Label(viewport)}");
        await s.StepAsync("Bottom navigation to Cases shows record cards instead of the table", async () =>
        {
            await page.GetByTestId("bottom-nav").GetByRole(AriaRole.Link, new() { Name = "Cases" }).TapAsync();
            await ExpectPageAsync(page, "Cases");
            await Expect(page.GetByTestId("case-cards")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("case-table")).ToBeHiddenAsync();
            await Expect(page.GetByTestId("bottom-nav").Locator("[aria-current=page]")).ToContainTextAsync("Cases");
            await ExpectNoHorizontalOverflowAsync(page, "case list");
        });
        await s.ScreenshotAsync($"Phone case list {Viewports.Label(viewport)}");
        await s.StepAsync("A case opens as a phone detail view with a back button", async () =>
        {
            await page.GetByTestId("case-cards").Locator("a").First.TapAsync();
            await Expect(page.GetByTestId("record-header")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("mobile-back")).ToHaveAttributeAsync("aria-label", "Back to Cases");
            await ExpectNoHorizontalOverflowAsync(page, "case detail");
        });
        await s.StepAsync("New case form: full-width touch inputs; the SSN box opens a numeric keyboard", async () =>
        {
            await page.GetByTestId("mobile-back").TapAsync();
            await page.GetByTestId("fab-new-case").TapAsync();
            await ExpectPageAsync(page, "New case");
            await Expect(page.Locator("#lookup-ssn")).ToHaveAttributeAsync("inputmode", "numeric");
            await Expect(page.Locator("#lookup-ssn")).ToHaveAttributeAsync("autocomplete", "off");
            var title = await page.Locator("#intake-title").BoundingBoxAsync();
            Assert.True(title is { Height: >= 44 } && title.Width >= viewport.Width - 64, $"Title input is {title?.Width}x{title?.Height}px");
            await ExpectNoHorizontalOverflowAsync(page, "new case");
        });
        await s.AxeAsync($"Phone new case {Viewports.Label(viewport)}");
        await s.StepAsync("More opens the site map as a sheet; Esc closes it and returns focus", async () =>
        {
            var more = page.GetByTestId("bottom-nav-more");
            await PressAsync(more);
            var sheet = page.GetByTestId("more-sheet");
            await Expect(sheet).ToBeVisibleAsync();
            await Expect(sheet.GetByRole(AriaRole.Link, new() { Name = "Case reports" })).ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Escape");
            await Expect(sheet).ToBeHiddenAsync();
            await E2ESession.ExpectFocusedAsync(more);
        });
        await s.StepAsync("The profile sheet shows the user and role, and offers Sign out", async () =>
        {
            await page.GetByTestId("mobile-profile").TapAsync();
            var sheet = page.GetByTestId("profile-sheet");
            await Expect(sheet).ToContainTextAsync(DemoAccounts.CaseWorker);
            await Expect(sheet).ToContainTextAsync("Role: Case worker");
            await sheet.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).TapAsync();
            await Expect(page.GetByTestId("signed-out")).ToBeVisibleAsync();
        });
        await s.StepAsync("No JavaScript errors", () => { Assert.Empty(s.ConsoleErrors); return Task.CompletedTask; });
        s.Complete();
    }

    private async Task WideAsync(ViewportSize viewport, [System.Runtime.CompilerServices.CallerMemberName] string method = "")
    {
        await using var s = await StartAsync(Meta($"Wide layout at {Viewports.Label(viewport)}", "Power Apps-style UI", ["R-4.2-12"],
            expected: "Sidebar (collapsible), header with identity, KPIs, charts, data grid, no overflow", method: method), viewport);
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Supervisor);
        await s.StepAsync("Sidebar and header shown; bottom navigation hidden", async () =>
        {
            await Expect(page.GetByTestId("side-nav")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("bottom-nav")).ToBeHiddenAsync();
            await Expect(page.GetByTestId("user-name")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("user-roles")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("fab-new-case")).ToBeHiddenAsync();
        });
        await s.StepAsync("All six KPI cards and the dashboard charts render", async () =>
        {
            foreach (var kpi in new[] { "kpi-total", "kpi-open", "kpi-pending", "kpi-overdue", "kpi-resolved", "kpi-mine" })
            {
                await Expect(page.GetByTestId(kpi)).ToBeVisibleAsync();
            }
            foreach (var chart in new[] { "chart-volume", "chart-status", "chart-resolution", "chart-priority", "chart-category" })
            {
                await Expect(page.GetByTestId(chart).Locator("svg.chart__svg")).ToBeVisibleAsync();
            }
            // Issue #21: bars and columns are links to the matching cases.
            await Expect(page.GetByTestId("chart-status").Locator("svg a.chart__point")).ToHaveCountAsync(5);
            await ExpectNoHorizontalOverflowAsync(page, "dashboard");
        });
        await s.ScreenshotAsync($"Dashboard {Viewports.Label(viewport)}");
        await s.StepAsync("The sidebar collapses to an icon rail and expands again", async () =>
        {
            var toggle = page.GetByTestId("nav-toggle");
            await PressAsync(toggle);
            await Expect(page.GetByTestId("side-nav")).ToHaveClassAsync(new Regex("app-nav--collapsed"));
            await ExpectNoHorizontalOverflowAsync(page, "collapsed navigation");
            await PressAsync(toggle);
        });
        await s.StepAsync("The case list shows the data grid", async () =>
        {
            await PressAsync(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "All cases" }));
            await Expect(page.GetByTestId("case-table")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("case-cards")).ToBeHiddenAsync();
            await ExpectNoHorizontalOverflowAsync(page, "case list");
        });
        await s.ScreenshotAsync($"Case list {Viewports.Label(viewport)}");
        await s.AxeAsync($"Case list {Viewports.Label(viewport)}");
        await s.StepAsync("No JavaScript errors", () => { Assert.Empty(s.ConsoleErrors); return Task.CompletedTask; });
        s.Complete();
    }
}
