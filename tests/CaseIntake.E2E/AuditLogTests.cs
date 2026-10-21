using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// Change 1, part 2 (ADR 0014), keyboard only: an administrator opens the audit log, filters it (focus stays on the
/// form), opens an entry, follows a user's history, and exports the filtered log as CSV. Every page is scanned with axe.
/// </summary>
public sealed class AuditLogTests : E2ETestBase
{
    [Test]
    public async Task Administrator_reads_filters_and_exports_the_audit_log_with_the_keyboard_only()
    {
        await using var s = await StartAsync(Meta("Administrator reads, filters and exports the audit log (keyboard only)", "Audit log",
            ["CR-1"], expected: "The sign-in is listed under Authentication, its entry opens, a user's history links back, and the CSV downloads"));
        var page = s.Page;

        await SignInAsync(s, DemoAccounts.Administrator);
        await s.StepAsync("Open the audit log from the side navigation", async () =>
        {
            await PressAsync(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "Audit log" }));
            await ExpectPageAsync(page, "Audit log");
            await Expect(page.GetByTestId("audit-row").First).ToBeVisibleAsync();
        });
        await s.AxeAsync("Audit log");

        await s.StepAsync("Filter to failed and successful sign-ins; focus stays on Apply", async () =>
        {
            await page.Locator("#audit-category").FocusAsync();
            await page.Locator("#audit-category").SelectOptionAsync("Authentication");
            var apply = page.GetByTestId("apply-audit-filters");
            await PressAsync(apply);
            await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("admin/audit\\?category=Authentication"));
            await Expect(page.GetByTestId("audit-row").First).ToContainTextAsync("Authentication");
            await E2ESession.ExpectFocusedAsync(apply);
            await Expect(Announcer(page)).ToContainTextAsync("entries");
        });

        await s.StepAsync("Open the newest entry", async () =>
        {
            await PressAsync(page.GetByTestId("audit-row").First.Locator("a").First);
            await Expect(page.Locator("h1")).ToHaveTextAsync("Signed in");
            await E2ESession.ExpectFocusedAsync(page.Locator("h1"));
            await Expect(page.GetByTestId("audit-entry")).ToContainTextAsync(DemoAccounts.Administrator);
        });
        await s.AxeAsync("Audit entry");

        await s.StepAsync("A user's page links to that user's history", async () =>
        {
            await GotoAsync(page, "/admin/users/6f1d1c52-0e3a-4c1b-9a51-000000000006");
            var history = page.GetByTestId("audit-history");
            await Expect(history).ToContainTextAsync("Signed in");
            await PressAsync(page.GetByTestId("history-all"));
            await ExpectPageAsync(page, "Audit log");
            await Expect(page.GetByTestId("audit-subject-filter")).ToContainTextAsync("User 6f1d1c52-0e3a-4c1b-9a51-000000000006");
        });

        await s.StepAsync("Export the filtered log as CSV", async () =>
        {
            var download = await page.RunAndWaitForDownloadAsync(() => PressAsync(page.GetByTestId("export-audit")));
            Assert.StartsWith("audit-log-", download.SuggestedFilename, StringComparison.Ordinal);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            await download.SaveAsAsync(path);
            var csv = await File.ReadAllTextAsync(path);
            File.Delete(path);
            Assert.StartsWith("Key,OccurredAtUtc,", csv.TrimStart('﻿'), StringComparison.Ordinal);
            Assert.Contains("SignedIn", csv);
        }, expected: "A CSV named audit-log-<time>Z.csv with the user's entries");
        s.Complete();
    }
}
