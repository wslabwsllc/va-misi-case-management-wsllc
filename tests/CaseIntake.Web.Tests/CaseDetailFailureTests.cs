using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit.Fakes;
using CaseIntake.Web.Components.Pages.Cases;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Case detail failure and recovery paths: load errors, refresh, assignment rejections, edit failures, conflict
/// recovery when the case was closed meanwhile, status change failures, attachment failures, and the audit and status
/// history panels on their own.
/// </summary>
[Category("Story:E5-S5")]
[Category("Story:E5-S6")]
[Category("Story:E5-S7")]
[Category("Req:R-4.2-6")]
[Category("Req:R-4.2-7")]
[Category("Req:R-4.2-8")]
public class CaseDetailFailureTests : WebTestContext
{
    private IRenderedComponent<CaseDetail> RenderCase(long id) => Render<CaseDetail>(p => p.Add(c => c.Id, id));

    private static AuditEntryDto Entry(long id, int hour, string action, CaseStatus? from = null, CaseStatus? to = null, string? details = null) =>
        new(id, 1, new DateTimeOffset(2026, 10, 1, hour, 0, 0, TimeSpan.Zero), "supervisor@example.test", action, from, to, details);

    // ---- Loading and load failures ----

    [Test]
    public async Task Veteran_attached_at_intake_shows_name_address_and_only_the_last_four_digits()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("With a veteran", veteran: new VeteranSnapshot("Rowan Calloway", "1 Main St\nSpringfield", "3456"));

        var cut = RenderCase(created.Id);

        var section = cut.WaitForElement("[data-testid=veteran-section]");
        Assert.Contains("Rowan Calloway", section.TextContent);
        Assert.Contains("1 Main St\nSpringfield", section.TextContent);
        Assert.Equal("Ending 3456", cut.Find("[data-testid=ssn-last4]").TextContent);
        Assert.DoesNotContain("No veteran was attached", section.TextContent);
    }

    [Test]
    public async Task A_case_without_a_veteran_says_so()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Without a veteran");
        var cut = RenderCase(created.Id);
        Assert.Contains("No veteran was attached at intake.", cut.WaitForElement("[data-testid=veteran-section]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=ssn-last4]"));
    }

    [Test]
    public async Task A_load_error_other_than_not_found_is_shown_with_a_title_and_no_record()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Forbidden case");
        CaseService.GetError = new Error(ErrorKind.Forbidden, "Forbidden", "You cannot open this case.");
        var notFound = false;
        Navigation.OnNotFound += (_, _) => notFound = true;

        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.Contains("The case could not be opened", cut.Find("[data-testid=detail-error]").TextContent));
        Assert.Contains("You cannot open this case.", cut.Find("[data-testid=detail-error]").TextContent);
        Assert.False(notFound);
        Assert.Empty(cut.FindAll("[data-testid=record-header]"));
        Assert.Equal($"Case {created.Id}", cut.Find("h1").TextContent.Trim());
    }

    [Test]
    public async Task An_unexpected_load_failure_shows_a_generic_message()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Broken case");
        CaseService.GetException = new InvalidOperationException("database offline");

        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.Contains("Something went wrong while loading the case. Try again.", cut.Find("[data-testid=detail-error]").TextContent));
        Assert.DoesNotContain("database offline", cut.Markup);
    }

    [Test]
    public async Task Loading_is_shown_until_the_case_arrives()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Slow case");
        CaseService.GetGate = new TaskCompletionSource();

        var cut = RenderCase(created.Id);

        Assert.Contains("Loading case…", cut.Find("[data-testid=loading]").TextContent);
        CaseService.GetGate.SetResult();
        cut.WaitForElement("[data-testid=record-header]");
        Assert.Empty(cut.FindAll("[data-testid=loading]"));
    }

    [Test]
    public async Task Audit_trail_failure_is_shown_in_the_history_tab_while_the_case_still_loads()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Audit unavailable");
        CaseService.AuditError = new Error(ErrorKind.ExternalUnavailable, "Down", "The audit trail is unavailable.");

        var cut = RenderCase(created.Id, "history");

        cut.WaitForAssertion(() => Assert.Contains("The audit trail is unavailable.", cut.Find("[data-testid=audit-error]").TextContent));
        Assert.Equal("Status: New", cut.Find("[data-testid=status-heading]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=audit-list]"));
        Assert.Equal("true", cut.Find("[data-testid=tab-history]").GetAttribute("aria-selected"));
    }

    private IRenderedComponent<CaseDetail> RenderCase(long id, string tab)
    {
        Navigation.NavigateTo($"cases/{id}?tab={tab}");
        return RenderCase(id);
    }

    // ---- Refresh ----

    [Test]
    public async Task Refresh_reloads_the_case_and_announces_it()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Before refresh");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=detail-refresh]");
        var actor = CurrentUser.Actor;
        CurrentUser.Actor = FakeCurrentUser.ForRole(AppRoles.Supervisor, "someone-else");
        await CaseService.UpdateAsync(created.Id, new UpdateCaseRequest("After refresh", null, created.Version), CancellationToken.None);
        CurrentUser.Actor = actor;

        cut.Find("[data-testid=detail-refresh]").Click();

        Assert.Equal("true", cut.Find("[data-testid=detail-refresh]").GetAttribute("aria-busy"));
        Assert.Contains("Refreshing…", cut.Find("[data-testid=detail-refresh]").TextContent);
        cut.WaitForAssertion(() => Assert.Equal("Case refreshed", Announcer.Message), TimeSpan.FromSeconds(5));
        Assert.Equal("After refresh", cut.Find("[data-testid=case-title]").TextContent);
        Assert.Contains("Refresh", cut.Find("[data-testid=detail-refresh]").TextContent);
        Assert.Equal("false", cut.Find("[data-testid=detail-refresh]").GetAttribute("aria-busy"));
    }

    // ---- Tabs ----

    [Test]
    public async Task Home_selects_the_first_tab_and_other_keys_change_nothing()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Keyboard tabs");
        var cut = RenderCase(created.Id, "attachments");
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("[data-testid=tab-attachments]").GetAttribute("aria-selected")));

        cut.Find("[data-testid=tab-attachments]").KeyDown("a");
        Assert.Equal("true", cut.Find("[data-testid=tab-attachments]").GetAttribute("aria-selected"));

        cut.Find("[data-testid=tab-attachments]").KeyDown("Home");
        Assert.Equal("true", cut.Find("[data-testid=tab-overview]").GetAttribute("aria-selected"));
        Assert.False(cut.Find("#panel-overview").HasAttribute("hidden"));
    }

    // ---- Assignment ----
    [TestCase(ErrorKind.Conflict, "This case was changed by someone else. The latest version is shown; try again.")]
    [TestCase(ErrorKind.Forbidden, "Your role cannot make this assignment.")]
    [TestCase(ErrorKind.ExternalUnavailable, "The assignment service is unavailable.")]
    public async Task A_rejected_take_explains_why_and_keeps_the_owner(ErrorKind kind, string message)
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Take me", CaseStatus.Triaged);
        var cut = RenderCase(created.Id);
        CaseService.NextAssignError = new Error(kind, kind.ToString(), "The assignment service is unavailable.");

        cut.WaitForElement("[data-testid=take-case]").Click();

        cut.WaitForAssertion(() => Assert.Contains("The owner was not changed", cut.Find("[data-testid=detail-error]").TextContent));
        Assert.Contains(message, cut.Find("[data-testid=detail-error]").TextContent);
        Assert.Equal("Unassigned", cut.Find("[data-testid=summary-owner]").TextContent.Trim());
        Assert.NotEmpty(cut.FindAll("[data-testid=take-case]"));
        Assert.Null(Announcer.Message);
    }

    [Test]
    public async Task A_field_error_from_the_assignment_is_shown_as_the_reason()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Assign me");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=assign-case]").Click();
        cut.Find("[data-testid=assign-owner]").Change("caseworker@example.test");
        CaseService.NextAssignError = new Error(ErrorKind.Validation, "Validation", "Some fields need attention.",
            new Dictionary<string, string[]> { [CaseFieldNames.AssignedTo] = ["The owner must be someone who works cases."] });

        cut.Find("[data-testid=assign-confirm]").Click();

        cut.WaitForAssertion(() => Assert.Contains("The owner must be someone who works cases.", cut.Find("[data-testid=detail-error]").TextContent));
        InteropModule.VerifyInvoke("closeDialog");
        Assert.Equal("Unassigned", cut.Find("[data-testid=summary-owner]").TextContent.Trim());
    }

    [Test]
    public async Task Cancelling_the_assign_dialog_closes_it_and_returns_focus()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Assign me later");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=assign-case]").Click();
        InteropModule.VerifyInvoke("showDialog");

        cut.Find("[data-testid=assign-cancel]").Click();

        InteropModule.VerifyInvoke("closeDialog");
        JSInterop.VerifyFocusAsyncInvoke();
        Assert.Empty(cut.FindAll("[data-testid=assign-owner]"));
        Assert.Equal("Unassigned", cut.Find("[data-testid=summary-owner]").TextContent.Trim());
    }

    [Test]
    public async Task The_assign_query_opens_the_dialog_for_supervisors_only()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Assign from the list");
        Navigation.NavigateTo($"cases/{created.Id}?assign=true");

        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=assign-owner] option").Count));
        InteropModule.VerifyInvoke("showDialog");

        SignInAs(AppRoles.CaseWorker);
        var worker = RenderCase(created.Id);
        worker.WaitForElement("[data-testid=take-case]");
        Assert.Empty(worker.FindAll("[data-testid=assign-owner]"));
    }

    // ---- Edit failures ----

    [Test]
    public async Task An_unexpected_save_failure_keeps_the_form_and_the_text()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("#edit-title").Input("Unsaved title text");
        CaseService.NextUpdateException = new InvalidOperationException("timeout");

        SubmitEdit(cut);

        cut.WaitForAssertion(() => Assert.Contains("Something went wrong. Your text is still in the form; try again.", cut.Find("[data-testid=detail-error]").TextContent));
        Assert.Contains("Your changes were not saved", cut.Find("[data-testid=detail-error]").TextContent);
        Assert.Equal("Unsaved title text", cut.Find("#edit-title").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid=reload-latest]"));
    }

    [Test]
    public async Task A_rejected_save_on_an_open_case_stays_in_edit_mode()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("#edit-title").Input("Not allowed title");
        CaseService.NextUpdateError = new Error(ErrorKind.Forbidden, "Forbidden", "You may not edit this case.");

        SubmitEdit(cut);

        cut.WaitForAssertion(() => Assert.Contains("You may not edit this case.", cut.Find("[data-testid=detail-error]").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-testid=edit-form]"));
    }

    [Test]
    public async Task Server_field_errors_on_the_description_and_unknown_fields_are_shown_in_the_summary()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        CaseService.NextUpdateError = new Error(ErrorKind.Validation, "Validation", "Fix these.", new Dictionary<string, string[]>
        {
            [CaseFieldNames.Description] = ["Description contains a blocked word."],
            ["Unexpected"] = ["Something else is wrong."],
        });

        SubmitEdit(cut);

        var summary = cut.Find("#error-summary").TextContent;
        Assert.Contains("Description contains a blocked word.", summary);
        Assert.Contains("Something else is wrong.", summary);
        Assert.Contains("Description contains a blocked word.", cut.Find("#edit-description-error").TextContent);
    }

    [Test]
    public async Task The_owner_list_loaded_for_editing_is_reused_by_the_assign_dialog()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Edit twice");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("[data-testid=cancel-edit]").Click();
        Services.GetRequiredService<InMemoryAssigneeDirectory>().Assignees.Clear();

        cut.Find("[data-testid=edit-case]").Click();
        cut.Find("[data-testid=cancel-edit]").Click();
        cut.Find("[data-testid=assign-case]").Click();

        Assert.Equal(["", "caseworker@example.test", "supervisor@example.test"], cut.FindAll("[data-testid=assign-owner] option").Select(o => o.GetAttribute("value")));
    }

    [Test]
    public async Task Reload_after_a_conflict_reports_a_failed_reload_and_keeps_the_option()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("#edit-title").Input("My unsaved title");
        CaseService.NextUpdateError = new Error(ErrorKind.Conflict, "VersionConflict", "This case was changed by someone else.");
        SubmitEdit(cut);
        CaseService.GetError = new Error(ErrorKind.ExternalUnavailable, "Down", "The case service is unavailable.");

        cut.Find("[data-testid=reload-latest]").Click();

        cut.WaitForAssertion(() => Assert.Contains("The latest version could not be loaded", cut.Find("[data-testid=detail-error]").TextContent));
        Assert.Contains("The case service is unavailable.", cut.Find("[data-testid=detail-error]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=reload-latest]"));
        Assert.Equal("My unsaved title", cut.Find("#edit-title").GetAttribute("value"));
    }

    [Test]
    public async Task Reload_after_a_conflict_on_a_case_closed_meanwhile_shows_the_unsaved_title_read_only()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Resolved case", CaseStatus.Resolved);
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("#edit-title").Input("Title that cannot be saved");
        CaseService.NextUpdateError = new Error(ErrorKind.Conflict, "VersionConflict", "This case was changed by someone else.");
        SubmitEdit(cut);
        var actor = CurrentUser.Actor;
        CurrentUser.Actor = FakeCurrentUser.ForRole(AppRoles.Supervisor, "another-supervisor");
        await CaseService.ChangeStatusAsync(created.Id, new ChangeStatusRequest(CaseStatus.Closed, created.Version), CancellationToken.None);
        CurrentUser.Actor = actor;

        cut.Find("[data-testid=reload-latest]").Click();

        cut.WaitForAssertion(() => Assert.Contains("This case was closed", cut.Find("[data-testid=detail-error]").TextContent));
        Assert.Contains("Your unsaved title was: \"Title that cannot be saved\".", cut.Find("[data-testid=detail-error]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=edit-form]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=closed-note]"));
    }

    // ---- Status change failures ----

    [Test]
    public async Task An_unexpected_status_failure_closes_the_dialog_and_shows_a_generic_alert()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Triage me");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=transition-Triaged]").Click();
        CaseService.NextStatusException = new InvalidOperationException("boom");

        cut.Find("[data-testid=confirm-status]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Something went wrong and the status was not changed. Try again.", cut.Find("[data-testid=status-error]").TextContent));
        InteropModule.VerifyInvoke("closeDialog");
        Assert.Equal("Status: New", cut.Find("[data-testid=status-heading]").TextContent);
    }

    [Test]
    public async Task Other_status_errors_show_the_service_message()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Triage me");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=transition-Triaged]").Click();
        CaseService.NextStatusError = new Error(ErrorKind.ExternalUnavailable, "Down", "The workflow service is down.");

        cut.Find("[data-testid=confirm-status]").Click();

        cut.WaitForAssertion(() => Assert.Contains("The workflow service is down.", cut.Find("[data-testid=status-error]").TextContent));
    }

    // ---- Attachments ----

    [Test]
    public async Task Attachment_list_failure_is_reported_without_hiding_the_upload_form()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Attachments down");
        AttachmentScript.ListException = new IOException("disk");

        var cut = RenderCase(created.Id, "attachments");

        cut.WaitForAssertion(() => Assert.Contains("The attachments could not be loaded.", cut.Find("[data-testid=attachments-error]").TextContent));
        Assert.NotEmpty(cut.FindAll("[data-testid=upload-form]"));
        Assert.Empty(cut.FindAll("[data-testid=attachments-empty]"));
    }

    public static TheoryData<Exception?, Error?, string> UploadFailures => new()
    {
        { null, new Error(ErrorKind.Validation, "TypeNotAllowed", "Files of this type are not allowed."), "Files of this type are not allowed." },
        { new IOException("reset"), null, "The file could not be read completely. Choose it again and retry." },
        { new InvalidOperationException("boom"), null, "Something went wrong and the file was not uploaded. Try again." },
    };
    [TestCaseSource(nameof(UploadFailures))]
    public async Task Upload_failures_are_explained_next_to_the_input(Exception? thrown, Error? error, string message)
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Attach to me");
        var cut = RenderCase(created.Id, "attachments");
        cut.WaitForElement("[data-testid=attachments-empty]");
        AttachmentScript.NextUploadException = thrown;
        AttachmentScript.NextUploadError = error;
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("hello", "notes.txt", contentType: "text/plain"));

        cut.Find("[data-testid=upload-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains(message, cut.Find("[data-testid=upload-error]").TextContent));
        Assert.Contains("The file was not uploaded", cut.Find("[data-testid=upload-error]").TextContent);
        Assert.Equal("true", cut.Find("#attachment-file").GetAttribute("aria-invalid"));
        Assert.NotEmpty(cut.FindAll("[data-testid=attachments-empty]"));
        Assert.Null(Announcer.Message);
    }

    [Test]
    public async Task Closed_cases_cannot_receive_files()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Closed case", CaseStatus.Closed);
        var cut = RenderCase(created.Id, "attachments");
        Assert.Contains("Closed cases cannot receive new files.", cut.WaitForElement("[data-testid=attachments-section]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=upload-form]"));
    }

    // ---- Audit trail and status history panels ----

    [Test]
    public void Audit_trail_shows_loading_empty_details_and_pages_back_to_newer_entries()
    {
        var loading = Render<AuditTrail>();
        Assert.Contains("Loading audit trail…", loading.Find("[data-testid=loading]").TextContent);

        var empty = Render<AuditTrail>(p => p.Add(a => a.Entries, []));
        Assert.Contains("No audit entries.", empty.Find("[data-testid=audit-section]").TextContent);

        List<AuditEntryDto> entries = [.. Enumerable.Range(1, 3).Select(i => Entry(i, i, AuditActions.Edited, details: $"Changed field {i}"))];
        entries.Add(Entry(4, 4, AuditActions.StatusChanged, CaseStatus.New, CaseStatus.Triaged));
        var cut = Render<AuditTrail>(p => p.Add(a => a.Entries, entries).Add(a => a.PageSize, 2));

        Assert.Equal("4 entries", cut.Find(".card__header .badge").TextContent);
        var first = cut.FindAll("[data-testid=audit-list] li");
        Assert.Equal("Status changed: New to Triaged", first[0].QuerySelector(".timeline__head")!.TextContent);
        Assert.Contains("Changed field 3", first[1].TextContent);
        cut.Find("[data-testid=audit-next]").Click();
        Assert.Equal("Showing 3–4 of 4 entries", cut.Find("[data-testid=audit-page-info]").TextContent);
        cut.Find("[data-testid=audit-previous]").Click();
        Assert.Equal("Showing 1–2 of 4 entries", cut.Find("[data-testid=audit-page-info]").TextContent);
        Assert.Equal("Showing 1–2 of 4 entries", cut.Find("[role=status]").TextContent);
    }

    [Test]
    public void Status_history_shows_loading_no_changes_reopened_badges_and_durations()
    {
        Assert.Contains("Loading status history…", Render<StatusHistory>().Find("[data-testid=loading]").TextContent);

        var none = Render<StatusHistory>(p => p.Add(s => s.Entries, [Entry(1, 1, AuditActions.Created)]));
        Assert.Contains("No status changes recorded.", none.Find("[data-testid=status-history]").TextContent);

        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        AuditEntryDto At(long id, TimeSpan offset, string action, CaseStatus from, CaseStatus to) =>
            new(id, 1, start + offset, "supervisor@example.test", action, from, to, null);
        var cut = Render<StatusHistory>(p => p.Add(s => s.Entries,
        [
            At(1, TimeSpan.Zero, AuditActions.StatusChanged, CaseStatus.New, CaseStatus.Triaged),
            At(2, TimeSpan.FromMinutes(30), AuditActions.StatusChanged, CaseStatus.Triaged, CaseStatus.InProgress),
            At(3, TimeSpan.FromHours(3), AuditActions.StatusChanged, CaseStatus.InProgress, CaseStatus.Resolved),
            At(4, TimeSpan.FromDays(2), AuditActions.Reopened, CaseStatus.Resolved, CaseStatus.InProgress),
        ]));

        var items = cut.FindAll("[data-testid=status-history] li");
        Assert.Equal(4, items.Count);
        Assert.Contains("Reopened", items[0].TextContent);
        Assert.Contains("Current status", items[0].TextContent);
        Assert.Contains("1 d 21 h in this status", items[1].TextContent);
        Assert.Contains("2 h 30 min in this status", items[2].TextContent);
        Assert.Contains("30 min in this status", items[3].TextContent);
        Assert.DoesNotContain("Reopened", items[1].TextContent);
    }
}
