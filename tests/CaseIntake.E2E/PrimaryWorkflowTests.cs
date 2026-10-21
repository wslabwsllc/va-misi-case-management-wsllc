using System.Text;
using System.Text.RegularExpressions;
using CaseIntake.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace CaseIntake.E2E;

/// <summary>
/// E5-S9: the primary workflow end to end, driven by keyboard navigation plus programmatic file selection: sign in, filter,
/// sort and page the list, create a case (with a validation error and a veteran lookup), edit it, take all five status
/// transitions including reopen, upload and download an attachment, sign out. Each step asserts the announced status or
/// error text and the focus target, and every page is scanned with axe.
/// </summary>
public sealed partial class PrimaryWorkflowTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    [GeneratedRegex(@"/cases/(\d+)$")]
    private static partial Regex CaseUrl();

    [Fact]
    public async Task Supervisor_completes_the_primary_workflow_by_keyboard()
    {
        await using var s = await StartAsync(Meta("Primary workflow by keyboard (Supervisor)", "E5-S9",
            ["R-4.2-1", "R-4.2-2", "R-4.2-3", "R-4.2-4", "R-4.2-5", "R-4.2-6", "R-4.2-7", "R-4.2-8", "R-4.2-9", "R-4.2-10", "R-4.2-12", "R-4.2-16", "R-4.3-1", "R-4.3-2", "R-4.3-3", "R-4.3-4", "R-4.3-5"],
            description: "Sign in, list controls, intake with validation and lookup, edit, all five transitions, attachment round trip, sign out.",
            preconditions: "Seeded demo data and accounts; the veteran-lookup mock is running.",
            expected: "Every step completes with the expected announcement and focus; no axe WCAG A/AA violations."));
        var page = s.Page;
        var title = "E2E intake " + Guid.NewGuid().ToString("N")[..8];

        await s.StepAsync("Sign-in page: the first Tab focuses the skip link", async () =>
        {
            await page.GotoAsync("/account/login");
            await page.Keyboard.PressAsync("Tab");
            await E2ESession.ExpectFocusedAsync(page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content" }));
        });
        await s.AxeAsync("Sign in");
        await SignInAsync(s, DemoAccounts.Supervisor);

        await s.StepAsync("Header shows the display name, the role and a Sign out button", async () =>
        {
            await Expect(page.GetByTestId("user-name")).ToHaveTextAsync(DemoAccounts.Supervisor);
            await Expect(page.GetByTestId("user-roles")).ToHaveTextAsync("Supervisor");
            await Expect(page.GetByTestId("sign-out")).ToBeVisibleAsync();
        });
        await s.StepAsync("After a page load, Tab reaches the skip link first and it moves focus to main", async () =>
        {
            await page.Keyboard.PressAsync("Tab");
            await E2ESession.ExpectFocusedAsync(page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content" }));
            await page.Keyboard.PressAsync("Enter");
            await E2ESession.ExpectFocusedAsync(page.Locator("main#main"));
        });
        await s.StepAsync("Landmarks: one banner, labelled main navigation, one main; the current page is marked", async () =>
        {
            await Expect(page.GetByRole(AriaRole.Banner)).ToHaveCountAsync(1);
            await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Main navigation" })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Main)).ToHaveCountAsync(1);
            await Expect(page.GetByTestId("side-nav").Locator("[aria-current=page]")).ToHaveTextAsync("Dashboard");
        });
        await s.ScreenshotAsync("Dashboard (Supervisor)");
        await s.AxeAsync("Dashboard");

        // ---- Case list: filter, sort, page, URL state ----
        await s.StepAsync("Open the case list from the navigation; focus moves to its h1", async () =>
        {
            await PressAsync(page.GetByTestId("side-nav").GetByRole(AriaRole.Link, new() { Name = "All cases" }));
            await ExpectPageAsync(page, "Cases");
            await Expect(page.GetByTestId("side-nav").Locator("[aria-current=page]")).ToHaveTextAsync("All cases");
        });
        await s.StepAsync("Filter by status In Progress with Space and Enter; the result count is announced", async () =>
        {
            var inProgress = page.GetByTestId("status-InProgress");
            await PressAsync(inProgress, " ");
            await Expect(inProgress).ToBeCheckedAsync();
            await PressAsync(page.GetByTestId("apply-filters"));
            await Expect(page).ToHaveURLAsync(new Regex("status=InProgress"));
            await Expect(page.GetByTestId("results-announcement")).ToContainTextAsync("Showing 1–");
            // Auto-retrying: wait until the re-rendered rows show only In Progress cases.
            var rows = page.GetByTestId("case-table").Locator("tbody [data-status]");
            await Expect(page.GetByTestId("case-table").Locator("tbody [data-status]:not([data-status=InProgress])")).ToHaveCountAsync(0);
            await Expect(rows.First).ToHaveAttributeAsync("data-status", "InProgress");
        }, expected: "Only In Progress cases; 'Showing 1–n of N cases' announced");
        await s.StepAsync("Sort by Title with Enter: aria-sort becomes ascending, then descending; focus stays on the header", async () =>
        {
            var sortTitle = page.GetByTestId("sort-Title");
            await PressAsync(sortTitle);
            await Expect(page.Locator("th[aria-sort]")).ToHaveAttributeAsync("aria-sort", "ascending");
            await Expect(page.Locator("th[aria-sort]")).ToContainTextAsync("Title");
            await E2ESession.ExpectFocusedAsync(sortTitle);
            await sortTitle.PressAsync("Enter");
            await Expect(page.Locator("th[aria-sort]")).ToHaveAttributeAsync("aria-sort", "descending");
            await E2ESession.ExpectFocusedAsync(sortTitle);
        });
        await s.StepAsync("Clear the filter and page forward; the page is announced and kept in the URL", async () =>
        {
            await PressAsync(page.GetByTestId("clear-filters"));
            await Expect(page).Not.ToHaveURLAsync(new Regex("status="));
            await Expect(page.GetByTestId("page-info")).ToContainTextAsync(new Regex(@"Page 1 of [2-9]"));
            await Expect(page.GetByTestId("page-next")).ToBeEnabledAsync();
            await PressAsync(page.GetByTestId("page-next"));
            await Expect(page.GetByTestId("page-info")).ToContainTextAsync("Page 2 of");
            await Expect(page.GetByTestId("results-announcement")).ToContainTextAsync("Page 2 of");
            await Expect(page).ToHaveURLAsync(new Regex("page=2"));
        });
        await s.StepAsync("Reloading the URL restores the same view (page 2, Title descending)", async () =>
        {
            await page.ReloadAsync();
            await Expect(page.GetByTestId("page-info")).ToContainTextAsync("Page 2 of");
            await Expect(page.Locator("th[aria-sort]")).ToHaveAttributeAsync("aria-sort", "descending");
            await WaitForCircuitAsync(page);
        });
        await s.AxeAsync("Case list");

        // ---- Intake with a validation error and veteran lookups ----
        await s.StepAsync("Open New case; focus is on its h1", async () =>
        {
            await PressAsync(page.GetByTestId("new-case"));
            await ExpectPageAsync(page, "New case");
        });
        await s.StepAsync("Submitting an empty form focuses the error summary; the field is marked invalid", async () =>
        {
            await PressAsync(page.GetByTestId("create-case"));
            var summary = page.Locator("#error-summary");
            await E2ESession.ExpectFocusedAsync(summary);
            await Expect(summary).ToContainTextAsync("Title is required.");
            await Expect(page.Locator("#intake-title")).ToHaveAttributeAsync("aria-invalid", "true");
            await Expect(page.Locator("#intake-title-error")).ToHaveTextAsync("Title is required.");
        }, expected: "Summary focused with 'Title is required.'; nothing saved");
        await s.AxeAsync("New case with a validation error");
        await s.StepAsync("The summary link moves focus to the Title field; type the case", async () =>
        {
            await PressAsync(page.Locator("#error-summary a"));
            await E2ESession.ExpectFocusedAsync(page.Locator("#intake-title"));
            await page.Keyboard.TypeAsync(title);
            await page.Keyboard.PressAsync("Tab");
            await E2ESession.ExpectFocusedAsync(page.Locator("#intake-description"));
            await page.Keyboard.TypeAsync("Created by the keyboard-only end-to-end test.");
        });
        await s.StepAsync("Lookup of an unknown SSN announces Not found and clears the SSN", async () =>
        {
            await page.Locator("#lookup-ssn").FocusAsync();
            await page.Keyboard.TypeAsync("999-99-9999");
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByTestId("lookup-status")).ToContainTextAsync("Not found");
            await Expect(page.Locator("#lookup-ssn")).ToHaveValueAsync(string.Empty);
        });
        await s.StepAsync("An invalid SSN format is reported in an alert", async () =>
        {
            await page.Locator("#lookup-ssn").FocusAsync();
            await page.Keyboard.TypeAsync("12345");
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByTestId("lookup-alert")).ToContainTextAsync("Invalid SSN format");
            await Expect(page.GetByTestId("lookup-alert")).ToHaveAttributeAsync("role", "alert");
        });
        await s.StepAsync("A known SSN finds the veteran; Use this veteran attaches them (SSN cleared, last four kept)", async () =>
        {
            await page.Locator("#lookup-ssn").FocusAsync();
            await page.Keyboard.TypeAsync("900-12-3456");
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByTestId("lookup-status")).ToContainTextAsync("Veteran found: Rowan Calloway");
            await Expect(page.Locator("#lookup-ssn")).ToHaveValueAsync(string.Empty);
            await PressAsync(page.GetByTestId("use-veteran"));
            await Expect(page.GetByTestId("selected-veteran")).ToContainTextAsync("Rowan Calloway, SSN ending 3456");
            await E2ESession.ExpectFocusedAsync(page.GetByRole(AriaRole.Button, new() { Name = "Remove veteran" }));
        });
        await s.ScreenshotAsync("Intake with lookup result");
        long caseId = 0;
        await s.StepAsync("Create the case: the detail page opens, its h1 is focused and 'Case N created' is announced", async () =>
        {
            await PressAsync(page.GetByTestId("create-case"));
            await Expect(page).ToHaveURLAsync(CaseUrl());
            caseId = long.Parse(CaseUrl().Match(page.Url).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            await ExpectPageAsync(page, $"Case {caseId}: {title}");
            await Expect(Announcer(page)).ToHaveTextAsync($"Case {caseId} created");
            await Expect(page.GetByTestId("ssn-last4")).ToHaveTextAsync("Ending 3456");
            await Expect(page.GetByTestId("audit-list")).ToContainTextAsync("Created");
        }, expected: "Case N created");
        await s.AxeAsync("Case detail");

        // ---- Edit (cancel, then save) ----
        await s.StepAsync("Edit then Cancel restores the values and returns focus to Edit", async () =>
        {
            await PressAsync(page.GetByTestId("edit-case"));
            await E2ESession.ExpectFocusedAsync(page.Locator("#edit-title"));
            await page.Keyboard.TypeAsync(" (discarded)");
            await PressAsync(page.GetByTestId("cancel-edit"));
            await Expect(page.GetByTestId("case-title")).ToHaveTextAsync(title);
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("edit-case"));
        });
        var newTitle = title + " edited";
        await s.StepAsync("Edit and Save: 'Changes saved' is announced and the view updates", async () =>
        {
            await PressAsync(page.GetByTestId("edit-case"));
            await E2ESession.ExpectFocusedAsync(page.Locator("#edit-title"));
            await page.Keyboard.PressAsync("End");
            await page.Keyboard.TypeAsync(" edited");
            await PressAsync(page.GetByTestId("save-case"));
            await Expect(Announcer(page)).ToHaveTextAsync("Changes saved");
            await Expect(page.GetByTestId("case-title")).ToHaveTextAsync(newTitle);
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("edit-case"));
        }, expected: "Changes saved");

        // ---- Status transitions: all five edges, with Esc cancel first ----
        await TransitionAsync(s, "Triaged", "Triaged", cancelFirst: true);
        await TransitionAsync(s, "InProgress", "In Progress");
        await TransitionAsync(s, "Resolved", "Resolved");
        await s.StepAsync("Resolved offers Reopen (move to In Progress) and Move to Closed", async () =>
        {
            await Expect(page.GetByTestId("transition-InProgress")).ToHaveTextAsync("Reopen (move to In Progress)");
            await Expect(page.GetByTestId("transition-Closed")).ToHaveTextAsync("Move to Closed");
        });
        await TransitionAsync(s, "InProgress", "In Progress");
        await TransitionAsync(s, "Resolved", "Resolved");

        // ---- Attachments: rejected type, then upload and download round trip ----
        await s.StepAsync("Open the Attachments tab with the keyboard (arrow keys move between tabs)", async () =>
        {
            var overview = page.GetByTestId("tab-overview");
            await overview.FocusAsync();
            await page.Keyboard.PressAsync("ArrowRight");
            await page.Keyboard.PressAsync("ArrowRight");
            var attachments = page.GetByTestId("tab-attachments");
            await E2ESession.ExpectFocusedAsync(attachments);
            await Expect(attachments).ToHaveAttributeAsync("aria-selected", "true");
            await Expect(page.GetByTestId("upload-form")).ToBeVisibleAsync();
        });
        await s.AxeAsync("Case detail, Attachments tab");
        await s.StepAsync("A chosen file can be removed before upload", async () =>
        {
            await page.GetByTestId("attachment-input").SetInputFilesAsync(new FilePayload { Name = "wrong-file.txt", MimeType = "text/plain", Buffer = [0x61] });
            await Expect(page.GetByTestId("selected-file")).ToContainTextAsync("wrong-file.txt");
            await PressAsync(page.GetByTestId("remove-selected-file"));
            await Expect(page.GetByTestId("selected-file")).ToHaveCountAsync(0);
            await Expect(Announcer(page)).ToHaveTextAsync("wrong-file.txt removed. No file is selected.");
        });
        await s.StepAsync("A file of a disallowed type is rejected with an alert and focus returns to the file input", async () =>
        {
            await page.GetByTestId("attachment-input").SetInputFilesAsync(new FilePayload { Name = "tool.exe", MimeType = "application/x-msdownload", Buffer = [0x4D, 0x5A, 0x90, 0x00] });
            await PressAsync(page.GetByTestId("upload-button"));
            await Expect(page.GetByTestId("upload-error")).ToContainTextAsync("not allowed");
            await E2ESession.ExpectFocusedAsync(page.Locator("#attachment-file"));
        });
        var content = Encoding.UTF8.GetBytes($"Synthetic attachment for case {caseId}.\n");
        await s.StepAsync("Upload a text file (programmatic file selection): 'File uploaded' is announced and the row appears", async () =>
        {
            await page.GetByTestId("attachment-input").SetInputFilesAsync(new FilePayload { Name = "e2e-evidence.txt", MimeType = "text/plain", Buffer = content });
            await PressAsync(page.GetByTestId("upload-button"));
            await Expect(Announcer(page)).ToHaveTextAsync("File uploaded: e2e-evidence.txt");
            await Expect(page.GetByTestId("attachments-table")).ToContainTextAsync("e2e-evidence.txt");
        });
        await s.StepAsync("Download it with the keyboard: the bytes match", async () =>
        {
            var link = page.GetByTestId("download-link").First;
            await Expect(link).ToHaveAccessibleNameAsync("Download e2e-evidence.txt");
            var download = await page.RunAndWaitForDownloadAsync(() => PressAsync(link));
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            await download.SaveAsAsync(path);
            Assert.Equal(content, await File.ReadAllBytesAsync(path));
            File.Delete(path);
        }, expected: "Downloaded bytes equal the uploaded bytes");

        await TransitionAsync(s, "Closed", "Closed");
        await s.StepAsync("A Closed case offers no status changes and no Edit", async () =>
        {
            await PressAsync(page.GetByTestId("tab-overview"));
            await Expect(page.GetByTestId("no-transitions")).ToHaveTextAsync("No status changes available");
            await Expect(page.GetByTestId("edit-case")).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("closed-note")).ToBeVisibleAsync();
        });
        await s.ScreenshotAsync("Closed case with audit trail");
        await SignOutAsync(s);
        s.Complete("Workflow completed: all five transitions, attachment round trip, sign out");
    }

    private static async Task TransitionAsync(E2ESession s, string target, string label, bool cancelFirst = false)
    {
        var page = s.Page;
        await s.StepAsync($"Change status to {label} in the confirmation dialog", async () =>
        {
            var trigger = page.GetByTestId($"transition-{target}");
            var dialog = page.GetByTestId("confirm-status-dialog");
            await PressAsync(trigger);
            await Expect(dialog).ToBeVisibleAsync();
            if (cancelFirst)
            {
                await s.AxeAsync("Status confirmation dialog");
                await page.Keyboard.PressAsync("Escape");
                await Expect(dialog).ToBeHiddenAsync();
                await E2ESession.ExpectFocusedAsync(trigger);
                await PressAsync(trigger);
                await Expect(dialog).ToBeVisibleAsync();
            }
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("confirm-status"));
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByTestId("status-heading")).ToHaveTextAsync($"Status: {label}");
            await E2ESession.ExpectFocusedAsync(page.GetByTestId("status-heading"));
            await Expect(Announcer(page)).ToHaveTextAsync($"Status changed to {label}");
            await Expect(page.GetByTestId("audit-list")).ToContainTextAsync(label);
        }, expected: $"Status changed to {label}");
    }

    [Fact]
    public async Task A_stale_save_reports_a_conflict_keeps_the_text_and_offers_reload()
    {
        await using var s = await StartAsync(Meta("Edit conflict keeps unsaved text", "E5-S5", ["R-4.2-7", "R-4.2-12"],
            expected: "Conflict alert with Reload latest; the unsaved text stays visible; focus moves to the alert, then to Title"));
        var page = s.Page;
        await SignInAsync(s, DemoAccounts.CaseWorker);
        long id = 0;
        await s.StepAsync("Create a case to edit", async () =>
        {
            await GotoAsync(page, "/cases/new");
            await page.Locator("#intake-title").FillAsync("E2E conflict " + Guid.NewGuid().ToString("N")[..8]);
            await PressAsync(page.GetByTestId("create-case"));
            await Expect(page).ToHaveURLAsync(CaseUrl());
            id = long.Parse(CaseUrl().Match(page.Url).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            // FocusOnNavigation moves focus to the new page's h1 after its first render. Wait for it, or it can take focus
            // back from the Edit button focused in the next step (seen under amd64 emulation).
            await E2ESession.ExpectFocusedAsync(page.Locator("h1"));
        });
        await s.StepAsync("Start editing in this session", async () =>
        {
            await PressAsync(page.GetByTestId("edit-case"));
            await E2ESession.ExpectFocusedAsync(page.Locator("#edit-title"));
            await page.Keyboard.PressAsync("End");
            await page.Keyboard.TypeAsync(" mine");
            await Expect(page.Locator("#edit-title")).ToHaveValueAsync(new Regex(" mine$"));
        });
        await s.StepAsync("Someone else saves the case first (second browser session)", async () =>
        {
            var other = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = E2ESettings.BaseUrl });
            try
            {
                var otherPage = await other.NewPageAsync();
                await otherPage.GotoAsync("/account/login");
                await otherPage.Locator("#login-email").FillAsync(DemoAccounts.Supervisor);
                await otherPage.Locator("#login-password").FillAsync(E2ESettings.DemoPassword);
                await otherPage.Locator("#login-password").PressAsync("Enter");
                await Expect(otherPage.Locator("h1")).ToHaveTextAsync("Dashboard");
                await GotoAsync(otherPage, $"/cases/{id}");
                await otherPage.GetByTestId("edit-case").ClickAsync();
                await otherPage.Locator("#edit-title").FillAsync($"Changed by the supervisor {id}");
                await otherPage.GetByTestId("save-case").ClickAsync();
                await Expect(otherPage.GetByTestId("case-title")).ToHaveTextAsync($"Changed by the supervisor {id}");
            }
            finally
            {
                await other.CloseAsync();
            }
        });
        await s.StepAsync("Saving the stale version shows the conflict alert with Reload latest; focus moves to the alert", async () =>
        {
            await PressAsync(page.GetByTestId("save-case"));
            var alert = page.GetByTestId("detail-error");
            await Expect(alert).ToContainTextAsync("Someone else changed this case");
            await E2ESession.ExpectFocusedAsync(alert);
            await Expect(page.Locator("#edit-title")).ToHaveValueAsync(new Regex(" mine$"));
        });
        await s.StepAsync("Reload latest shows the latest version and keeps the unsaved text visible", async () =>
        {
            await PressAsync(page.GetByTestId("reload-latest"));
            await Expect(page.Locator("#edit-title")).ToHaveValueAsync($"Changed by the supervisor {id}");
            await Expect(page.GetByTestId("unsaved-draft")).ToContainTextAsync(" mine");
            await E2ESession.ExpectFocusedAsync(page.Locator("#edit-title"));
            // The toast appears about 60 ms after the render (StatusAnnouncerOutlet.RefreshGap). Wait for it so the axe scan
            // below sees the settled page, not a toast that starts fading in mid-scan.
            await Expect(Announcer(page)).ToHaveTextAsync("Latest version loaded. Your unsaved text is shown below the form.");
        });
        await s.AxeAsync("Case detail with a conflict");
        s.Complete();
    }
}
