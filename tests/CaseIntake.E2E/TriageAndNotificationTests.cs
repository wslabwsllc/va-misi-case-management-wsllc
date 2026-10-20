using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// Issues #21 and #22 in the Docker-hosted app: intake with priority, category, owner and due date; the case summary,
/// workflow stepper and tabs; notifications reaching another signed-in user live (badge and unread list); and dashboard
/// charts that show values on hover and drill through to the case list.
/// </summary>
public sealed partial class TriageAndNotificationTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    [GeneratedRegex(@"/cases/(\d+)$")]
    private static partial Regex CaseUrl();

    [Fact]
    public async Task Triage_fields_notifications_and_chart_drill_through_work_end_to_end()
    {
        await using var s = await StartAsync(Meta("Triage fields, live notifications and chart drill-through", "Issue-21/22",
            ["R-4.2-4", "R-4.2-6", "R-4.2-11", "R-4.2-12"],
            description: "Supervisor creates an urgent case owned by the case worker; the case worker's badge and notifications update live; dashboard charts show values and drill through.",
            preconditions: "Seeded demo data and accounts.",
            expected: "Summary shows the triage fields; the case worker sees an unread notification for their case without reloading; the Triaged bar opens the Triaged list."));
        var page = s.Page;
        var title = "E2E triage " + Guid.NewGuid().ToString("N")[..8];
        await SignInAsync(s, DemoAccounts.Supervisor);

        // The case worker, signed in in a second browser session, starts with no unread notifications.
        var other = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = E2ESettings.BaseUrl });
        try
        {
            var worker = await other.NewPageAsync();
            await s.StepAsync("Case worker (second session) marks everything as read", async () =>
            {
                await worker.GotoAsync("/account/login");
                await worker.Locator("#login-email").FillAsync(DemoAccounts.CaseWorker);
                await worker.Locator("#login-password").FillAsync(E2ESettings.DemoPassword);
                await worker.Locator("#login-password").PressAsync("Enter");
                await Expect(worker.Locator("h1")).ToHaveTextAsync("Dashboard");
                await GotoAsync(worker, "/notifications");
                var markAll = worker.GetByTestId("mark-all-read");
                if (await markAll.IsEnabledAsync())
                {
                    await markAll.ClickAsync();
                }
                await Expect(worker.GetByTestId("nav-unread")).ToHaveCountAsync(0);
            });

            long caseId = 0;
            await s.StepAsync("Intake with Urgent priority and Claims category, left unassigned", async () =>
            {
                await GotoAsync(page, "/cases/new");
                await page.Locator("#intake-title").FillAsync(title);
                await page.Locator("#intake-priority").SelectOptionAsync("Urgent");
                await Expect(page.Locator("#intake-due-hint")).ToContainTextAsync("Urgent priority: 2 days");
                await page.Locator("#intake-category").SelectOptionAsync("Claims");
                await PressAsync(page.GetByTestId("create-case"));
                await Expect(page).ToHaveURLAsync(CaseUrl());
                caseId = long.Parse(CaseUrl().Match(page.Url).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                await E2ESession.ExpectFocusedAsync(page.Locator("h1"));   // after-navigation focus first, then Assign (next step)
                await Expect(page.GetByTestId("summary-owner")).ToHaveTextAsync("Unassigned");
            }, expected: "Case created");
            await s.StepAsync("The supervisor assigns it to the case worker with the Assign dialog (keyboard)", async () =>
            {
                await PressAsync(page.GetByTestId("assign-case"));
                var dialog = page.GetByTestId("assign-dialog");
                await Expect(dialog).ToBeVisibleAsync();
                await page.GetByTestId("assign-owner").SelectOptionAsync(DemoAccounts.CaseWorker);
                await PressAsync(page.GetByTestId("assign-confirm"));
                await Expect(dialog).ToBeHiddenAsync();
                await Expect(Announcer(page)).ToHaveTextAsync($"The case is assigned to {DemoAccounts.CaseWorker}");
                await E2ESession.ExpectFocusedAsync(page.GetByTestId("assign-case"));
            }, expected: "Assigned; focus back on Reassign");
            await s.StepAsync("The case worker sees 'Assigned to you' without reloading", async () =>
            {
                await Expect(worker.GetByTestId("nav-unread")).ToBeVisibleAsync(new() { Timeout = 15_000 });
                var assignment = worker.GetByTestId("notifications-list").Locator("li").Filter(new() { HasText = title }).First;
                await Expect(assignment).ToContainTextAsync("assigned this case to you");
                await Expect(assignment.GetByTestId("assigned-to-you")).ToHaveTextAsync("Assigned to you");
                await worker.GetByTestId("mark-all-read").ClickAsync();
                await Expect(worker.GetByTestId("nav-unread")).ToHaveCountAsync(0);
            });
            await s.StepAsync("The summary strip and the workflow stepper show the new case", async () =>
            {
                await Expect(page.GetByTestId("summary-priority")).ToContainTextAsync("Urgent");
                await Expect(page.GetByTestId("summary-category")).ToHaveTextAsync("Claims");
                await Expect(page.GetByTestId("summary-owner")).ToHaveTextAsync(DemoAccounts.CaseWorker);
                await Expect(page.GetByTestId("summary-due")).ToContainTextAsync("Due in 2 days");
                await Expect(page.GetByTestId("workflow-stepper").Locator("[aria-current=step]")).ToContainTextAsync("New");
            });
            await s.ScreenshotAsync("Case detail with triage summary and workflow stepper");
            await s.AxeAsync("Case detail (summary, stepper, tabs)");

            await s.StepAsync("Triage the case; the case worker's badge updates without a reload", async () =>
            {
                await PressAsync(page.GetByTestId("transition-Triaged"));
                await PressAsync(page.GetByTestId("confirm-status"));
                await Expect(page.GetByTestId("status-heading")).ToHaveTextAsync("Status: Triaged");
                await Expect(worker.GetByTestId("nav-unread")).ToBeVisibleAsync(new() { Timeout = 15_000 });
                await Expect(worker.GetByTestId("nav-unread")).ToContainTextAsync("unread");
            }, expected: "Unread badge appears in the other session");
            await s.StepAsync("The case worker's notifications list the change as unread on their case", async () =>
            {
                var first = worker.GetByTestId("notifications-list").Locator("li").Filter(new() { HasText = title }).First;
                await Expect(first).ToHaveAttributeAsync("data-unread", "true");
                await Expect(first).ToContainTextAsync("moved it from New to Triaged");
                await Expect(first).ToContainTextAsync("Your case");
                await worker.GetByTestId("mark-all-read").ClickAsync();
                await Expect(worker.GetByTestId("nav-unread")).ToHaveCountAsync(0);
                await Expect(worker.GetByTestId("unread-summary")).ToHaveTextAsync("All caught up: no unread notifications.");
            });
            await s.StepAsync("The Workflow tab shows the status history", async () =>
            {
                await PressAsync(page.GetByTestId("tab-workflow"));
                await Expect(page.GetByTestId("status-history")).ToContainTextAsync("Triaged");
                await Expect(page.GetByTestId("status-history")).ToContainTextAsync("Current status");
            });

            await s.StepAsync("Dashboard: hovering a bar shows its value, Escape hides it; the bar opens the matching case list", async () =>
            {
                await GotoAsync(page, "/");
                var triaged = page.GetByTestId("chart-status").Locator("a.chart__point").Nth(1);
                await triaged.Locator(".chart__hit").HoverAsync(new() { Force = true });   // the bar scales on hover, so skip the stability wait
                // The tooltip is in the chart's top layer (named by data-tip), shown by js/app.js; Escape hides it (WCAG 1.4.13).
                var tip = page.Locator($"#{await triaged.GetAttributeAsync("data-tip")}");
                await Expect(tip).ToHaveCSSAsync("opacity", "1");
                await Expect(tip).ToContainTextAsync("Triaged:");
                await Expect(page.GetByTestId("chart-status").Locator(".chart__tip.is-shown")).ToHaveCountAsync(1);
                await page.Keyboard.PressAsync("Escape");
                await Expect(tip).ToHaveCSSAsync("opacity", "0");
                await PressAsync(triaged);
                await Expect(page).ToHaveURLAsync(new Regex("cases\\?status=Triaged$"));
                await Expect(page.Locator("h1")).ToHaveTextAsync("Cases");
                await Expect(page.GetByTestId("case-table").Locator("tbody [data-status]:not([data-status=Triaged])")).ToHaveCountAsync(0);
            }, expected: "Tooltip visible, hidden by Escape; Cases page filtered to Triaged");
            await s.StepAsync("The Overdue card opens the overdue list sorted by due date", async () =>
            {
                await GotoAsync(page, "/");
                await PressAsync(page.GetByTestId("kpi-overdue"));
                await Expect(page).ToHaveURLAsync(new Regex("overdue=true"));
                await Expect(page.GetByTestId("filter-overdue")).ToBeCheckedAsync();
                await Expect(page.Locator("th[aria-sort]")).ToContainTextAsync("Due date");
            });
            s.Complete($"Case {caseId}: triage, live notification and drill-through verified");
        }
        finally
        {
            await other.CloseAsync();
        }
    }
}
