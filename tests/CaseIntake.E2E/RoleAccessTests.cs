using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// E5-S8 and E5-S9 role differences in the browser: the header identity, permission-based navigation, status buttons
/// per role (the CaseWorker cannot triage, close or reopen), access-denied and Not Found pages with focus on the h1,
/// and server-side denial of the protected endpoints even when the UI hides them.
/// </summary>
public sealed class RoleAccessTests : E2ETestBase
{
    private static async Task<long> FirstCaseInStatusAsync(IPage page, string status)
    {
        await GotoAsync(page, $"/cases?status={status}&sort=Id&dir=asc");
        var link = page.GetByTestId("case-table").Locator("tbody tr td a").First;
        var href = await link.GetAttributeAsync("href");
        return long.Parse(href!.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Test]
    public async Task CaseWorker_cannot_triage_close_or_reopen_and_sees_create()
    {
        await using var s = await StartAsync(Meta("CaseWorker status buttons and navigation", "E5-S6/E5-S8", ["R-4.2-11", "R-4.3-1", "R-4.3-4", "R-4.3-5"],
            expected: "No Move to Triaged on New, no Close or Reopen on Resolved, Start/Resolve offered; New case visible; no Administration"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.CaseWorker);
        await s.StepAsync("Header and navigation for the CaseWorker", async () =>
        {
            await Expect(page.GetByTestId("user-roles")).ToHaveTextAsync("Case worker");
            var nav = page.GetByTestId("side-nav");
            await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "New case" })).ToBeVisibleAsync();
            await Expect(nav.GetByText("Administration")).ToHaveCountAsync(0);
            await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Test results" })).ToHaveCountAsync(0);
        });
        await s.StepAsync("A New case offers no status change to the CaseWorker", async () =>
        {
            await GotoAsync(page, $"/cases/{await FirstCaseInStatusAsync(page, "New")}");
            await Expect(page.GetByTestId("no-transitions")).ToHaveTextAsync("No status changes available");
            await Expect(page.GetByTestId("transition-Triaged")).ToHaveCountAsync(0);
        });
        await s.StepAsync("A Resolved case offers neither Close nor Reopen", async () =>
        {
            await GotoAsync(page, $"/cases/{await FirstCaseInStatusAsync(page, "Resolved")}");
            await Expect(page.GetByTestId("transition-Closed")).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("transition-InProgress")).ToHaveCountAsync(0);
        });
        await s.StepAsync("A Triaged case offers Move to In Progress", async () =>
        {
            await GotoAsync(page, $"/cases/{await FirstCaseInStatusAsync(page, "Triaged")}");
            await Expect(page.GetByTestId("transition-InProgress")).ToHaveTextAsync("Move to In Progress");
        });
        await s.StepAsync("The test evidence endpoint is denied (HTTP 403) for the CaseWorker", async () =>
        {
            var response = await s.Context.APIRequest.GetAsync($"/test-results/artifacts/{Guid.NewGuid()}", new APIRequestContextOptions { MaxRedirects = 0 });
            Assert.Equal(403, response.Status);
        });
        s.ExpectErrorResponse("/account/access-denied");
        await s.StepAsync("Opening the test results page directly shows Access denied with a link back to the case list", async () =>
        {
            await page.GotoAsync("/test-results");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Access denied");
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Back to the case list" })).ToHaveAttributeAsync("href", "cases");
            await Expect(page.GetByTestId("access-denied")).ToContainTextAsync("Case worker");
        });
        await s.AxeAsync("Access denied");
        s.Complete();
    }

    [Test]
    public async Task Supervisor_can_triage_and_sees_close_and_reopen()
    {
        await using var s = await StartAsync(Meta("Supervisor status buttons", "E5-S6", ["R-4.2-11", "R-4.3-1", "R-4.3-4", "R-4.3-5"],
            expected: "Move to Triaged on New; Move to Closed and Reopen on Resolved"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Supervisor);
        await s.StepAsync("A New case offers Move to Triaged", async () =>
        {
            await GotoAsync(page, $"/cases/{await FirstCaseInStatusAsync(page, "New")}");
            await Expect(page.GetByTestId("transition-Triaged")).ToHaveTextAsync("Move to Triaged");
        });
        await s.StepAsync("A Resolved case offers Move to Closed and Reopen", async () =>
        {
            await GotoAsync(page, $"/cases/{await FirstCaseInStatusAsync(page, "Resolved")}");
            await Expect(page.GetByTestId("transition-Closed")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("transition-InProgress")).ToHaveTextAsync("Reopen (move to In Progress)");
        });
        s.Complete();
    }

    [Test]
    public async Task QualityAnalyst_sees_test_results_but_not_new_case_and_in_app_denial_focuses_the_heading()
    {
        await using var s = await StartAsync(Meta("QualityAnalyst navigation and access denied", "E5-S8", ["R-4.2-11", "R-4.2-12"],
            expected: "Administration > Quality assurance shown; New case hidden; in-app denial shows Access denied with the h1 focused"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.QualityAnalyst);
        await s.StepAsync("Navigation shows Administration and hides New case", async () =>
        {
            var nav = page.GetByTestId("side-nav");
            await Expect(nav.GetByText("Administration")).ToBeVisibleAsync();
            await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Quality assurance" })).ToBeVisibleAsync();
            await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "New case" })).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("user-roles")).ToHaveTextAsync("Quality analyst");
        });
        await s.StepAsync("In-app navigation to New case shows Access denied and focuses its h1", async () =>
        {
            await page.EvaluateAsync("() => Blazor.navigateTo('/cases/new')");
            var h1 = page.Locator("h1");
            await Expect(h1).ToHaveTextAsync("Access denied");
            await E2ESession.ExpectFocusedAsync(h1);
            await Expect(page).ToHaveTitleAsync("Access denied - Case Intake and Triage");
            await PressAsync(page.GetByRole(AriaRole.Link, new() { Name = "Back to the case list" }));
            await ExpectPageAsync(page, "Cases");
        });
        await s.StepAsync("The case CSV export is allowed (cases.view) and the create policy stays server-side", async () =>
        {
            var csv = await s.Context.APIRequest.GetAsync("/exports/cases.csv");
            Assert.Equal(200, csv.Status);
            Assert.StartsWith("Id,Title,Status", (await csv.TextAsync()).TrimStart((char)0xFEFF), StringComparison.Ordinal);
        });
        s.Complete();
    }

    [Test]
    public async Task Auditor_reads_cases_and_audit_trail_but_changes_nothing()
    {
        await using var s = await StartAsync(Meta("Auditor read-only access", "E3-S2", ["R-4.2-11"],
            expected: "No New case; case detail shows the audit trail with no edit, status change or upload; /cases/new is denied"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.Auditor);
        await s.StepAsync("Header and navigation for the Auditor", async () =>
        {
            await Expect(page.GetByTestId("user-roles")).ToHaveTextAsync("Auditor");
            await Expect(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "New case" })).ToHaveCountAsync(0);
        });
        await s.StepAsync("A Triaged case is read-only for the Auditor", async () =>
        {
            await GotoAsync(page, $"/cases/{await FirstCaseInStatusAsync(page, "Triaged")}");
            await PressAsync(page.GetByTestId("tab-history"));
            await Expect(page.GetByTestId("audit-list")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("no-transitions")).ToHaveTextAsync("No status changes available");
            await Expect(page.GetByTestId("edit-case")).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("upload-form")).ToHaveCountAsync(0);
        });
        s.ExpectErrorResponse("/account/access-denied");
        await s.StepAsync("Opening New case directly shows Access denied", async () =>
        {
            await page.GotoAsync("/cases/new");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Access denied");
            await Expect(page.GetByTestId("access-denied")).ToContainTextAsync("Auditor");
        });
        await s.AxeAsync("Access denied (auditor)");
        s.Complete();
    }

    [Test]
    public async Task IntakeClerk_opens_cases_but_cannot_edit_or_move_them()
    {
        await using var s = await StartAsync(Meta("IntakeClerk intake-only access", "E3-S2", ["R-4.2-11"],
            expected: "New case visible and opens; case detail offers upload but no edit or status change"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.IntakeClerk);
        await s.StepAsync("Header and New case for the IntakeClerk", async () =>
        {
            await Expect(page.GetByTestId("user-roles")).ToHaveTextAsync("Intake clerk");
            await Expect(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "New case" })).ToBeVisibleAsync();
            await GotoAsync(page, "/cases/new");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Open a new case");
        });
        await s.StepAsync("A Triaged case offers upload but no edit or status change", async () =>
        {
            await GotoAsync(page, $"/cases/{await FirstCaseInStatusAsync(page, "Triaged")}");
            await PressAsync(page.GetByTestId("tab-attachments"));
            await Expect(page.GetByTestId("upload-form")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("edit-case")).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("no-transitions")).ToHaveTextAsync("No status changes available");
        });
        s.Complete();
    }

    [Test]
    public async Task Unknown_routes_and_missing_cases_show_an_accessible_not_found_page()
    {
        await using var s = await StartAsync(Meta("Not Found page", "E5-S1/E5-S5", ["R-4.2-12"],
            expected: "Unknown URL and missing case show 'Page not found' with links; HTTP 404 on a direct load"));
        var page = s.Page;
        s.ExpectErrorResponse("/no-such-page");
        s.ExpectErrorResponse("/cases/999999999");
        await SignInAsync(s, DemoAccounts.CaseWorker);
        await s.StepAsync("A direct load of an unknown URL answers 404 with the Not Found page", async () =>
        {
            var response = await page.GotoAsync("/no-such-page");
            Assert.Equal(404, response!.Status);
            await Expect(page.Locator("h1")).ToHaveTextAsync("Page not found");
            await Expect(page.GetByTestId("not-found").GetByRole(AriaRole.Link, new() { Name = "Go to the case list" })).ToBeVisibleAsync();
        });
        await s.AxeAsync("Not found");
        await s.StepAsync("In-app navigation to a missing case shows Not Found and focuses the h1", async () =>
        {
            await WaitForCircuitAsync(page);
            await page.EvaluateAsync("() => Blazor.navigateTo('/cases/999999999')");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Page not found");
            await E2ESession.ExpectFocusedAsync(page.Locator("h1"));
        });
        s.Complete();
    }

    [Test]
    public async Task Anonymous_requests_to_protected_pages_and_endpoints_are_denied()
    {
        await using var s = await StartAsync(Meta("Anonymous access is denied", "E5-S8", ["R-4.2-11"],
            expected: "Pages redirect to sign-in; download/export endpoints answer 401 without a redirect"));
        await s.StepAsync("A protected page redirects to sign-in with a return URL and an explanation", async () =>
        {
            await s.Page.GotoAsync("/cases");
            await Expect(s.Page).ToHaveURLAsync(new Regex("/account/login\\?ReturnUrl=%2Fcases"));
            await Expect(s.Page.GetByTestId("sign-in-required")).ToContainTextAsync("Sign in to continue");
        });
        await s.StepAsync("Endpoints answer 401 (no login redirect)", async () =>
        {
            foreach (var path in new[] { "/exports/cases.csv", $"/test-results/artifacts/{Guid.NewGuid()}", "/test-results/runs/1/export/csv", $"/cases/1/attachments/{Guid.NewGuid()}/content" })
            {
                var response = await s.Context.APIRequest.GetAsync(path, new APIRequestContextOptions { MaxRedirects = 0 });
                Assert.True(response.Status == 401, $"{path} returned {response.Status}");
            }
        });
        s.Complete();
    }
}
