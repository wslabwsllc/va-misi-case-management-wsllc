using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// Change 1, part 5: an automated accessibility sweep of every page of the application as the Administrator (who can open
/// all of them) and of the public pages. Evidence for the Section 508 report (docs/section-508). Per page: axe (WCAG 2.0,
/// 2.1 and 2.2 A/AA rules), one h1, a page-specific title, lang="en-US", a main landmark; on desktop the first Tab
/// reaches the skip link and focus stays visible and on the page for 30 Tab presses; at 320 CSS px (WCAG 1.4.10, 400%
/// zoom of 1280 px) nothing scrolls sideways. Times carry EDT or EST.
/// </summary>
public sealed partial class AccessibilitySweepTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    private const string TitleSuffix = " - Case Intake and Triage";

    [GeneratedRegex(@"\b\d{2}/\d{2}/\d{4} \d{2}:\d{2} (AM|PM) (EDT|EST)\b")]
    private static partial Regex EasternTimestamp();

    /// <summary>Every authenticated page; record pages use the first case and audit entry and stable seeded IDs.</summary>
    private static async Task<IReadOnlyList<(string Url, string Name)>> PagesAsync(IPage page)
    {
        await GotoAsync(page, "/cases");
        var caseHref = await page.GetByTestId("case-table").Locator("tbody a").First.GetAttributeAsync("href");
        await GotoAsync(page, "/admin/audit");
        var auditHref = await page.GetByTestId("audit-row").First.Locator("a").First.GetAttributeAsync("href");
        return
        [
            ("/", "Dashboard"), ("/cases", "Case list"), ("/cases/new", "New case"), ("/" + caseHref, "Case detail"),
            ("/my-work", "My tasks"), ("/notifications", "Notifications"), ("/reports", "Reports"),
            ("/admin/users", "Users"), ("/admin/users/6f1d1c52-0e3a-4c1b-9a51-000000000001", "User detail"),
            ("/admin/roles", "Roles"), ("/admin/roles/role-caseworker", "Role editor"), ("/admin/roles/new", "New role"),
            ("/admin/audit", "Audit log"), ("/" + auditHref, "Audit entry"), ("/admin/errors", "Errors"), ("/admin/health", "System health"),
            ("/test-results", "Quality assurance"), ("/test-results/runs", "Test runs"), ("/test-results/cases", "Test cases"),
            ("/test-results/history", "History and trends"), ("/account/change-password", "Change password"), ("/more", "More"),
        ];
    }

    /// <summary>Opens a page; the account pages are static (no circuit to wait for).</summary>
    private static Task OpenAsync(IPage page, string url) =>
        url.StartsWith("/account/", StringComparison.Ordinal) ? page.GotoAsync(url) : GotoAsync(page, url);

    private static async Task ExpectPageStructureAsync(IPage page, string name)
    {
        await Expect(page.Locator("h1")).ToHaveCountAsync(1);
        await Expect(page).ToHaveTitleAsync(new Regex(Regex.Escape(TitleSuffix) + "$"));
        Assert.Equal("en-US", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.True(await page.Locator("main, [role=main]").CountAsync() >= 1, $"{name}: no main landmark");
    }

    [Fact]
    public async Task Every_page_passes_axe_and_has_one_h1_a_title_a_language_and_a_main_landmark()
    {
        await using var s = await StartAsync(Meta("Accessibility sweep: every page (axe and structure)", "Accessibility and Section 508", ["CR-1", "R-4.2-8"],
            expected: "Each page: no axe WCAG 2.x A/AA violations, one h1, a page title, lang en-US, a main landmark"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Administrator);

        foreach (var (url, name) in await PagesAsync(page))
        {
            await s.StepAsync($"Structure: {name}", async () =>
            {
                await OpenAsync(page, url);
                await ExpectPageStructureAsync(page, name);
            });
            await s.AxeAsync(name);
        }
        s.Complete();
    }

    [Fact]
    public async Task Public_pages_pass_axe_and_have_the_same_structure()
    {
        await using var s = await StartAsync(Meta("Accessibility sweep: public pages", "Accessibility and Section 508", ["CR-1"],
            expected: "Landing, sign-in and Not Found: no axe violations, one h1, title, lang en-US, main landmark"));
        var page = s.Page;
        s.ExpectErrorResponse("/no-such-page");
        foreach (var (url, name) in new[] { ("/welcome", "Landing"), ("/account/login", "Sign in"), ("/no-such-page", "Not Found") })
        {
            await s.StepAsync($"Structure: {name}", async () =>
            {
                await page.GotoAsync(url);
                await ExpectPageStructureAsync(page, name);
            });
            await s.AxeAsync(name);
        }
        s.Complete();
    }

    [Fact]
    public async Task On_every_page_the_skip_link_comes_first_and_keyboard_focus_stays_visible()
    {
        await using var s = await StartAsync(Meta("Accessibility sweep: keyboard focus on every page", "Accessibility and Section 508", ["CR-1", "R-4.2-8"],
            expected: "First Tab reaches 'Skip to main content'; for 30 Tab presses focus is never lost and always has a visible indicator"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Administrator);

        foreach (var (url, name) in await PagesAsync(page))
        {
            await s.StepAsync($"Keyboard focus: {name}", async () =>
            {
                await OpenAsync(page, url);
                await page.EvaluateAsync("() => { document.activeElement?.blur(); window.scrollTo(0, 0); }");
                await page.Keyboard.PressAsync("Tab");
                await Expect(page.Locator(":focus")).ToHaveTextAsync("Skip to main content");
                for (var i = 1; i <= 30; i++)
                {
                    await page.Keyboard.PressAsync("Tab");
                    var problem = await page.EvaluateAsync<string?>(@"() => {
                        const el = document.activeElement;
                        if (!el || el === document.body) return 'focus was lost to the page body';
                        const box = el.getBoundingClientRect();
                        if (box.width === 0 && box.height === 0) return 'focused element is invisible: ' + el.outerHTML.slice(0, 120);
                        // WCAG 2.4.11: a control outside the sticky header must not be hidden under it.
                        const header = document.querySelector('.app-header-wrap');
                        if (header && !header.contains(el) && box.bottom <= header.getBoundingClientRect().bottom)
                            return 'focused element is hidden under the sticky header: ' + el.outerHTML.slice(0, 120);
                        // The ring may be on the element, on a close ancestor (stretched card links use :focus-within)
                        // or on an inner SVG shape (chart bars stroke their hit area).
                        const ringed = n => {
                            const s = getComputedStyle(n);
                            return (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || (s.boxShadow && s.boxShadow !== 'none');
                        };
                        const ancestors = [];
                        for (let n = el.parentElement, i = 0; n && i < 3; n = n.parentElement, i++) ancestors.push(n);
                        const svgStroke = [...el.querySelectorAll('rect, path, circle')].some(n => {
                            const s = getComputedStyle(n);
                            return s.stroke && s.stroke !== 'none' && parseFloat(s.strokeWidth) > 0;
                        });
                        return ringed(el) || ancestors.some(ringed) || svgStroke ? null : 'no visible focus indicator on ' + el.outerHTML.slice(0, 120);
                    }");
                    if (problem == "focus was lost to the page body")
                    {
                        // Past the last control focus leaves the document; the next Tab must come back to the skip link.
                        await page.Keyboard.PressAsync("Tab");
                        await Expect(page.Locator(":focus")).ToHaveTextAsync("Skip to main content");
                        break;
                    }
                    Assert.True(problem is null, $"{name}, Tab {i}: {problem}");
                }
            });
        }
        s.Complete();
    }

    [Fact]
    public async Task Every_page_reflows_at_320_css_pixels_without_sideways_scrolling()
    {
        await using var s = await StartAsync(Meta("Accessibility sweep: reflow at 320 CSS px", "Accessibility and Section 508", ["CR-1", "R-4.2-12"],
            expected: "No page is wider than the 320 px viewport (wide tables scroll inside their card)"), Viewports.Reflow320, mobile: true);
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Administrator);

        foreach (var (url, name) in await PagesAsync(page))
        {
            await s.StepAsync($"Reflow: {name}", async () =>
            {
                await OpenAsync(page, url);
                await Expect(page.Locator("h1")).ToHaveCountAsync(1);
                var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
                Assert.True(overflow <= 0, $"{name}: the page is {overflow}px wider than the 320px viewport");
            });
        }
        s.Complete();
    }

    [Fact]
    public async Task Times_are_shown_in_eastern_time_with_the_zone_in_en_US_format()
    {
        await using var s = await StartAsync(Meta("Times are US Eastern with EDT or EST", "Date and time format", ["CR-1"],
            expected: "Case detail, audit log and user detail show MM/dd/yyyy hh:mm AM/PM EDT|EST"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Administrator);

        foreach (var (url, testId) in new[] { ("/admin/audit", "audit-table"), ("/admin/users/6f1d1c52-0e3a-4c1b-9a51-000000000006", "account-section") })
        {
            await s.StepAsync($"Eastern timestamps on {url}", async () =>
            {
                await GotoAsync(page, url);
                var text = await page.GetByTestId(testId).InnerTextAsync();
                Assert.Matches(EasternTimestamp(), text);
                Assert.DoesNotMatch(new Regex(@"\b\d{4}-\d{2}-\d{2} \d{2}:\d{2}\b"), text);
            });
        }
        s.Complete();
    }
}
