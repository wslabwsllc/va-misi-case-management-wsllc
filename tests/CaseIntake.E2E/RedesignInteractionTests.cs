using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// The redesign's interaction rules in a real browser: interactive cards show their "View details" hint on hover and on
/// keyboard focus (and are one link); a chart card's background opens the larger view while its legend keeps the
/// drill-through; dialogs return focus; filters apply as they change and announce the count; dates are typed as
/// MM-DD-YYYY; Cancel in the submit confirmation keeps every value.
/// </summary>
public sealed class RedesignInteractionTests : E2ETestBase
{
    [Test]
    [Category("A11y:Wcag22")]
    [Category("Wcag:2.2:2.4.7:AA")]
    [Category("Method:Automated")]
    [Category("Workflow:Dashboard, All cases, New case")]
    public async Task Cards_charts_filters_and_the_submit_confirmation_behave_as_designed()
    {
        await using var s = await StartAsync(Meta("Redesign interactions: cards, charts, filters, confirmation", "Redesign", ["R-4.2-11", "R-4.2-12"],
            expected: "Card hint on hover and focus; chart background opens the dialog and Esc returns focus; filters auto-apply; MM-DD-YYYY dates; Cancel keeps values"),
            Viewports.Desktop1440);
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Supervisor);

        await s.StepAsync("A KPI card reveals 'View details' on keyboard focus and on hover, and is a single link", async () =>
        {
            var card = page.GetByTestId("kpi-open");
            var hint = card.Locator(".icard__more");
            await Expect(hint).ToHaveCSSAsync("opacity", "0");
            await card.FocusAsync();
            await Expect(hint).ToHaveCSSAsync("opacity", "1");
            await Expect(card).ToHaveCSSAsync("cursor", "pointer");
            await page.Locator("body").FocusAsync();
            await page.Mouse.MoveAsync(0, 0);
            await card.HoverAsync();
            await Expect(hint).ToHaveCSSAsync("opacity", "1");
            await Expect(card.Locator("a, button, input, select")).ToHaveCountAsync(0);
            await Expect(card).ToHaveAccessibleNameAsync(new Regex("^Open cases \\d+"));
        });
        await s.ScreenshotAsync("Dashboard with a hovered KPI card");
        await s.StepAsync("Clicking a chart card's background opens the expanded chart; Esc closes it", async () =>
        {
            var chart = page.GetByTestId("chart-status");
            await chart.Locator(".card__backdrop-action").ClickAsync(new() { Position = new() { X = 20, Y = 20 } });
            var dialog = page.GetByTestId("chart-status-dialog");
            await Expect(dialog).ToBeVisibleAsync();
            await Expect(dialog.Locator("h2")).ToHaveTextAsync("Cases by status");
            await page.Keyboard.PressAsync("Escape");
            await Expect(dialog).ToBeHiddenAsync();
            // Focus returns to the chart's Expand button (the keyboard path); wait for it so the next step's focus is not
            // taken back by this restore.
            await Expect(page.GetByTestId("chart-status-expand")).ToBeFocusedAsync();
        });
        await s.StepAsync("The Expand button opens the same dialog by keyboard and focus returns to it", async () =>
        {
            var expand = page.GetByTestId("chart-volume-expand");
            await PressAsync(expand);
            var dialog = page.GetByTestId("chart-volume-dialog");
            await Expect(dialog).ToBeVisibleAsync();
            await s.AxeAsync("Expanded chart dialog");
            await page.Keyboard.PressAsync("Escape");
            await Expect(dialog).ToBeHiddenAsync();
            await E2ESession.ExpectFocusedAsync(expand);
        });

        await s.StepAsync("All cases: a filter applies as soon as it changes and the count is announced", async () =>
        {
            await GotoAsync(page, "/cases");
            await Expect(page.GetByTestId("apply-filters")).ToHaveCountAsync(0);
            await page.GetByTestId("filter-priority").SelectOptionAsync("Urgent");
            await Expect(page).ToHaveURLAsync(new Regex("priority=Urgent"));
            await Expect(page.GetByTestId("results-announcement")).ToContainTextAsync(new Regex("(cases? found|No cases match)"));
        });
        await s.StepAsync("A due date is typed as MM-DD-YYYY and kept in that format", async () =>
        {
            var dueFrom = page.Locator("#filter-due-from");
            await Expect(page.Locator("#filter-due-from-format")).ToHaveTextAsync("Format: MM-DD-YYYY");
            await dueFrom.FillAsync("1-5-2026");
            await dueFrom.PressAsync("Tab");
            await Expect(page).ToHaveURLAsync(new Regex("dueFrom=2026-01-05"));
            await Expect(dueFrom).ToHaveValueAsync("01-05-2026");
            await PressAsync(page.GetByTestId("clear-filters"));
            await Expect(page).Not.ToHaveURLAsync(new Regex("dueFrom|priority"));
        });
        await s.StepAsync("A due date is picked from the calendar button", async () =>
        {
            // Headless Chromium draws no picker, so record that the button opened it (and that the CSP allowed the script).
            await page.EvaluateAsync("HTMLInputElement.prototype.showPicker = function () { window.pickerOpenedFor = this.type; }");
            await PressAsync(page.Locator("#filter-due-from ~ [data-date-picker]"));
            Assert.Equal("date", await page.EvaluateAsync<string>("window.pickerOpenedFor"));
            await page.Locator("#filter-due-from + .date-picker-proxy").FillAsync("2026-02-03");
            await Expect(page).ToHaveURLAsync(new Regex("dueFrom=2026-02-03"));
            await Expect(page.Locator("#filter-due-from")).ToHaveValueAsync("02-03-2026");
            await PressAsync(page.GetByTestId("clear-filters"));
            await Expect(page).Not.ToHaveURLAsync(new Regex("dueFrom"));
        });
        await s.ScreenshotAsync("All cases");

        await s.StepAsync("New case: Cancel in 'Are you sure you want to submit?' keeps every value and creates nothing", async () =>
        {
            await GotoAsync(page, "/cases/new");
            await page.Locator("#intake-title").FillAsync("Kept after cancel");
            await page.Locator("#intake-description").FillAsync("First line\nSecond line");
            await page.GetByTestId("intake-priority-High").CheckAsync();
            await PressAsync(page.GetByTestId("create-case"));
            var dialog = page.GetByTestId("confirm-intake");
            await Expect(dialog.Locator("h2")).ToHaveTextAsync("Are you sure you want to submit?");
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("confirm-intake-cancel"));
            await s.ScreenshotAsync("Submit confirmation");
            await s.AxeAsync("Submit confirmation dialog");
            await PressAsync(page.GetByTestId("confirm-intake-cancel"));
            await Expect(dialog).ToBeHiddenAsync();
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("create-case"));
            await Expect(page).ToHaveURLAsync(new Regex("/cases/new$"));
            await Expect(page.Locator("#intake-title")).ToHaveValueAsync("Kept after cancel");
            await Expect(page.Locator("#intake-description")).ToHaveValueAsync("First line\nSecond line");
            await Expect(page.GetByTestId("intake-priority-High")).ToBeCheckedAsync();
            await Expect(page.Locator("#intake-description")).Not.ToHaveValueAsync(new Regex("object", RegexOptions.IgnoreCase));
        });
        await s.StepAsync("Cancel and Remove buttons are red with white text", async () =>
        {
            var cancel = page.GetByTestId("intake-cancel");
            await Expect(cancel).ToHaveCSSAsync("background-color", "rgb(220, 38, 38)");
            await Expect(cancel).ToHaveCSSAsync("color", "rgb(255, 255, 255)");
            await GotoAsync(page, "/admin/users");
            var remove = page.GetByTestId("remove-user").First;
            await Expect(remove).ToHaveCSSAsync("background-color", "rgb(220, 38, 38)");
            await Expect(remove).ToHaveCSSAsync("color", "rgb(255, 255, 255)");
        });
        await s.ScreenshotAsync("Users");
        await s.StepAsync("No JavaScript errors", () => { Assert.Empty(s.ConsoleErrors); return Task.CompletedTask; });
        s.Complete();
    }
}
