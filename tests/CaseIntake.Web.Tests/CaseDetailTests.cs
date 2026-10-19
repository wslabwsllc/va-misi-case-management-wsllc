using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Pages.Cases;
using Microsoft.AspNetCore.Components.Forms;

namespace CaseIntake.Web.Tests;

/// <summary>E5-S5 detail and edit (view/edit toggling, focus, conflict), E5-S6 status controls per role, E5-S7 attachments.</summary>
[Trait("Category", "Story:E5-S5")]
[Trait("Category", "Story:E5-S6")]
[Trait("Category", "Story:E5-S7")]
[Trait("Category", "Req:R-4.2-6")]
[Trait("Category", "Req:R-4.2-7")]
[Trait("Category", "Req:R-4.2-8")]
[Trait("Category", "Req:R-4.2-11")]
public class CaseDetailTests : WebTestContext
{
    private IRenderedComponent<CaseDetail> RenderCase(long id) => Render<CaseDetail>(p => p.Add(c => c.Id, id));

    [Fact]
    public async Task Shows_id_title_description_status_audit_fields_and_the_audit_trail()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Detail page case", description: "Line one\nLine two");

        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.Equal("Detail page case", cut.Find("[data-testid=case-title]").TextContent));
        Assert.Equal(created.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find("[data-testid=case-id]").TextContent);
        Assert.Contains("(read-only)", cut.Find("[data-testid=details-section]").TextContent);
        Assert.Equal("Line one\nLine two", cut.Find("[data-testid=case-description]").TextContent);
        Assert.Contains("UTC", cut.Find("[data-testid=record-header]").TextContent);
        Assert.Contains("Created", cut.Find("[data-testid=audit-list]").TextContent);
        Assert.Empty(cut.Find("[data-testid=audit-error]").TextContent.Trim());
        Assert.Empty(cut.Find("[data-testid=detail-error]").TextContent.Trim());
        Assert.Equal("Status: New", cut.Find("[data-testid=status-heading]").TextContent);
    }

    [Fact]
    public async Task Edit_moves_focus_to_title_and_Cancel_restores_values_and_focus()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();

        Assert.Equal("Original title", cut.Find("#edit-title").GetAttribute("value"));
        JSInterop.VerifyFocusAsyncInvoke();
        cut.Find("#edit-title").Input("Changed but cancelled");
        cut.Find("[data-testid=cancel-edit]").Click();

        Assert.Equal("Original title", cut.Find("[data-testid=case-title]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=edit-form]"));
        Assert.Equal(2, JSInterop.Invocations.Count(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase)));
        Assert.Empty(CaseService.Updates);
    }

    [Fact]
    public async Task Save_persists_announces_changes_saved_and_sends_the_version()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("#edit-title").Input("Updated title");

        cut.Find("[data-testid=edit-form]").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Updated title", cut.Find("[data-testid=case-title]").TextContent));
        Assert.Equal("Changes saved", Announcer.Message);
        Assert.Equal(created.Version, CaseService.Updates.Single().Version);
    }

    [Fact]
    public async Task Invalid_edit_shows_the_summary_and_does_not_save()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("#edit-title").Input("ab");

        cut.Find("[data-testid=edit-form]").Submit();

        Assert.Equal("Title must be between 5 and 200 characters.", cut.Find("#error-summary a").TextContent);
        Assert.Empty(CaseService.Updates);
    }

    [Fact]
    public async Task Conflict_explains_offers_reload_and_keeps_the_unsaved_text()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Original title");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        cut.Find("#edit-title").Input("My unsaved title");
        CaseService.NextUpdateError = new Error(ErrorKind.Conflict, "VersionConflict", "This case was changed by someone else.");

        cut.Find("[data-testid=edit-form]").Submit();

        Assert.Contains("Someone else changed this case", cut.Find("[data-testid=detail-error]").TextContent);
        Assert.Equal("My unsaved title", cut.Find("#edit-title").GetAttribute("value"));

        cut.Find("[data-testid=reload-latest]").Click();

        cut.WaitForAssertion(() => Assert.Contains("My unsaved title", cut.Find("[data-testid=unsaved-draft]").TextContent));
        Assert.Equal("Original title", cut.Find("#edit-title").GetAttribute("value"));
        cut.Find("[data-testid=use-draft]").Click();
        Assert.Equal("My unsaved title", cut.Find("#edit-title").GetAttribute("value"));
    }

    [Fact]
    public async Task No_edit_button_without_cases_edit_or_on_a_closed_case()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var open = await SeedCaseAsync("Readable by QA");
        var closed = await SeedCaseAsync("Closed case", CaseStatus.Closed);

        var qa = RenderCase(open.Id);
        qa.WaitForAssertion(() => Assert.NotEmpty(qa.FindAll("[data-testid=case-title]")));
        Assert.Empty(qa.FindAll("[data-testid=edit-case]"));

        SignInAs(AppRoles.Supervisor);
        var cut = RenderCase(closed.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=closed-note]")));
        Assert.Empty(cut.FindAll("[data-testid=edit-case]"));
    }

    [Fact]
    public void A_missing_case_shows_the_not_found_page()
    {
        SignInAs(AppRoles.CaseWorker);
        var notFound = false;
        Navigation.OnNotFound += (_, _) => notFound = true;

        RenderCase(999);

        Assert.True(notFound);
    }

    // ---- E5-S6 status controls per role ----

    [Fact]
    public async Task Supervisor_sees_Move_to_Triaged_on_a_new_case()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Triage me");
        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.Equal("Move to Triaged", cut.Find("[data-testid=transition-Triaged]").TextContent.Trim()));
        Assert.Equal("group", cut.Find("[data-testid=change-status-group]").GetAttribute("role"));
        Assert.Equal("Change status", cut.Find("#change-status-label").TextContent);
    }

    [Fact]
    public async Task CaseWorker_sees_no_status_change_on_a_new_case()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Cannot triage");
        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.Equal("No status changes available", cut.Find("[data-testid=no-transitions]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=transition-Triaged]"));
    }

    [Fact]
    public async Task Resolved_case_offers_close_and_reopen_to_the_supervisor_but_nothing_to_the_case_worker()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Resolved case", CaseStatus.Resolved);
        var supervisor = RenderCase(created.Id);
        supervisor.WaitForAssertion(() => Assert.Equal("Reopen (move to In Progress)", supervisor.Find("[data-testid=transition-InProgress]").TextContent.Trim()));
        Assert.Equal("Move to Closed", supervisor.Find("[data-testid=transition-Closed]").TextContent.Trim());

        SignInAs(AppRoles.CaseWorker);
        var worker = RenderCase(created.Id);
        worker.WaitForAssertion(() => Assert.Equal("No status changes available", worker.Find("[data-testid=no-transitions]").TextContent));
    }

    [Fact]
    public async Task Confirmed_transition_updates_status_refreshes_audit_focuses_the_heading_and_announces()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Triage me");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=transition-Triaged]").Click();

        InteropModule.VerifyInvoke("showDialog");
        Assert.Contains("Change status to Triaged?", cut.Find("[data-testid=confirm-status-dialog]").TextContent);
        cut.Find("[data-testid=confirm-status]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Status: Triaged", cut.Find("[data-testid=status-heading]").TextContent));
        Assert.Equal("Status changed to Triaged", Announcer.Message);
        Assert.Contains("Triaged", cut.Find("[data-testid=audit-list]").TextContent);
        InteropModule.VerifyInvoke("closeDialog");
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task Cancelling_the_dialog_returns_focus_to_the_trigger_and_changes_nothing()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Triage me");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=transition-Triaged]").Click();

        cut.Find("[data-testid=confirm-cancel]").Click();

        JSInterop.VerifyFocusAsyncInvoke();
        Assert.Equal("Status: New", cut.Find("[data-testid=status-heading]").TextContent);
        Assert.Null(Announcer.Message);
    }

    [Theory]
    [InlineData(ErrorKind.Conflict, "changed by someone else")]
    [InlineData(ErrorKind.Forbidden, "not allowed to make this status change")]
    [InlineData(ErrorKind.InvalidTransition, "cannot move to that status")]
    public async Task Rejected_transition_shows_an_alert_and_refreshes_the_status(ErrorKind kind, string text)
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Triage me");
        var cut = RenderCase(created.Id);
        CaseService.NextStatusError = new Error(kind, kind.ToString(), "The case cannot move to that status from its current status.");
        cut.WaitForElement("[data-testid=transition-Triaged]").Click();

        cut.Find("[data-testid=confirm-status]").Click();

        cut.WaitForAssertion(() => Assert.Contains(text, cut.Find("[data-testid=status-error]").TextContent));
        Assert.Equal("Status: New", cut.Find("[data-testid=status-heading]").TextContent);
    }

    // ---- E5-S7 attachments ----

    [Fact]
    public async Task Attachments_panel_states_allowed_types_and_size_and_lists_uploads_with_download_links()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Attach to me");
        var cut = RenderCase(created.Id);

        var hint = cut.WaitForElement("#attachment-file-hint").TextContent;
        Assert.Contains("Allowed types: .txt, .pdf", hint);
        Assert.Contains("Maximum size: 1 KiB", hint);
        Assert.Contains("Choose a file to attach", cut.Find("label[for=attachment-file]").TextContent);

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("hello", "notes.txt", contentType: "text/plain"));
        cut.Find("[data-testid=upload-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains("notes.txt", cut.Find("[data-testid=attachments-table]").TextContent));
        Assert.Equal("File uploaded: notes.txt", Announcer.Message);
        var link = cut.Find("[data-testid=download-link]");
        Assert.StartsWith($"/cases/{created.Id}/attachments/", link.GetAttribute("href"), StringComparison.Ordinal);
        Assert.EndsWith("/content", link.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Contains("Download notes.txt", link.TextContent.Replace("  ", " ", StringComparison.Ordinal).Trim());
        Assert.Equal("notes.txt", link.GetAttribute("download"));
    }

    [Fact]
    public async Task Upload_without_a_file_or_over_the_limit_explains_why_and_returns_focus_to_the_input()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Attach to me");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=upload-form]").Submit();
        Assert.Contains("Choose a file to upload first.", cut.Find("[data-testid=upload-error]").TextContent);
        Assert.Equal("true", cut.Find("#attachment-file").GetAttribute("aria-invalid"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task Upload_controls_are_hidden_without_upload_permission_but_the_list_remains()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var created = await SeedCaseAsync("Read only for QA");
        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=attachments-section]")));
        Assert.Empty(cut.FindAll("[data-testid=upload-form]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=attachments-empty]"));
    }
}
