using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit.Fakes;
using CaseIntake.Web.Components.Layout;
using CaseIntake.Web.Components.Pages;
using CaseIntake.Web.Components.Pages.Cases;
using CaseIntake.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Error = CaseIntake.Contracts.Common.Error;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Issues #21 and #22: the redesigned case detail (summary strip, workflow stepper, tabs, triage fields), removing a
/// chosen file before upload, the audit trail pager, intake triage fields, notifications with unread state and the
/// navigation badge, and the page progress bar.
/// </summary>
[Category("Story:Issue-21")]
[Category("Story:Issue-22")]
[Category("Req:R-4.2-6")]
[Category("Req:R-4.2-12")]
public class CaseTriageAndNotificationUiTests : WebTestContext
{
    private IRenderedComponent<CaseDetail> RenderCase(long id) => Render<CaseDetail>(p => p.Add(c => c.Id, id));

    // ---------- Case detail (#22) ----------

    [Test]
    public async Task Summary_strip_shows_the_triage_fields_and_the_due_state()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Triaged detail", CaseStatus.Triaged,
            triage: new CaseTriage(CasePriority.High, CaseCategory.ContactDetails, "caseworker@example.test", new DateOnly(2026, 10, 16)));

        var cut = RenderCase(created.Id);

        cut.WaitForAssertion(() => Assert.Contains("High", cut.Find("[data-testid=summary-priority]").TextContent));
        Assert.Equal("Contact details", cut.Find("[data-testid=summary-category]").TextContent.Trim());
        Assert.Equal("caseworker@example.test", cut.Find("[data-testid=summary-owner]").TextContent.Trim());
        var due = cut.Find("[data-testid=summary-due]");
        Assert.Contains("10-16-2026", due.TextContent);
        Assert.Contains("Overdue by 3 days", due.TextContent);   // today is 2026-10-19 (UTC)
        Assert.Contains("cell-overdue", due.ClassName);
        Assert.Equal("Case summary", cut.Find("[data-testid=record-header]").GetAttribute("aria-label"));
    }

    [Test]
    public async Task Workflow_stepper_marks_completed_stages_and_the_current_step()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Stepper case", CaseStatus.InProgress);

        var cut = RenderCase(created.Id);

        var steps = cut.WaitForElement("[data-testid=workflow-stepper]").QuerySelectorAll("li");
        Assert.Equal(["New", "Triaged", "InProgress", "Resolved", "Closed"], steps.Select(s => s.GetAttribute("data-stage")));
        Assert.Contains("stepper__step--complete", steps[0].ClassName);
        Assert.Contains("stepper__step--complete", steps[1].ClassName);
        Assert.Equal("step", steps[2].GetAttribute("aria-current"));
        Assert.Contains("Current", steps[2].TextContent);
        Assert.Equal("3", steps[2].QuerySelector(".stepper__marker")!.TextContent.Trim());   // numbered; completed stages show a check
        Assert.Contains("since 10-19-2026", steps[2].TextContent);   // when it was entered, from the audit trail
        Assert.Contains("Not started", steps[3].TextContent);
        Assert.Single(cut.FindAll("[data-testid=workflow-stepper] [aria-current=step]"));
        // The status controls sit with the stepper and keep their heading and buttons.
        Assert.Equal("Status: In Progress", cut.Find("[data-testid=status-heading]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=status-section] [data-testid=transition-Resolved]"));
        Assert.Contains("Next step: move to resolved", cut.Find("[data-testid=next-step]").TextContent);
    }

    [Test]
    public async Task Tabs_follow_the_tabs_pattern_with_arrow_keys_home_and_end()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Tabbed case");
        var cut = RenderCase(created.Id);

        var tabs = cut.WaitForElement("[role=tablist]").QuerySelectorAll("[role=tab]");
        Assert.Equal(["Overview", "Attachments", "History"], tabs.Select(t => t.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]));
        Assert.Equal("Case sections", cut.Find("[role=tablist]").GetAttribute("aria-label"));
        Assert.Equal("true", cut.Find("[data-testid=tab-overview]").GetAttribute("aria-selected"));
        Assert.Equal("0", cut.Find("[data-testid=tab-overview]").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("[data-testid=tab-history]").GetAttribute("tabindex"));
        Assert.False(cut.Find("#panel-overview").HasAttribute("hidden"));
        Assert.True(cut.Find("#panel-attachments").HasAttribute("hidden"));
        Assert.Equal("tab-overview", cut.Find("#panel-overview").GetAttribute("aria-labelledby"));

        cut.Find("[data-testid=tab-overview]").KeyDown("ArrowRight");
        Assert.Equal("true", cut.Find("[data-testid=tab-attachments]").GetAttribute("aria-selected"));
        Assert.False(cut.Find("#panel-attachments").HasAttribute("hidden"));
        Assert.True(cut.Find("#panel-overview").HasAttribute("hidden"));

        cut.Find("[data-testid=tab-attachments]").KeyDown("End");
        Assert.Equal("true", cut.Find("[data-testid=tab-history]").GetAttribute("aria-selected"));
        cut.Find("[data-testid=tab-history]").KeyDown("ArrowRight");   // wraps around
        Assert.Equal("true", cut.Find("[data-testid=tab-overview]").GetAttribute("aria-selected"));
        cut.Find("[data-testid=tab-overview]").KeyDown("ArrowLeft");
        Assert.Equal("true", cut.Find("[data-testid=tab-history]").GetAttribute("aria-selected"));

        cut.Find("[data-testid=tab-attachments]").Click();
        Assert.False(cut.Find("#panel-attachments").HasAttribute("hidden"));
        Assert.Equal("0", cut.Find("[data-testid=attachments-count]").TextContent.Trim()[..1]);
    }

    [Test]
    public async Task Edit_saves_priority_category_and_due_date_and_shows_the_owner_read_only()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Edit my triage");
        var cut = RenderCase(created.Id);

        cut.WaitForElement("[data-testid=edit-case]").Click();
        var owner = cut.Find("#edit-owner");
        Assert.True(owner.HasAttribute("readonly"));   // the owner changes only through Assign / Take case / Hand back
        Assert.Contains("Change the owner with Assign, Take case or Hand back.", cut.Find("#edit-owner-hint").TextContent);
        Assert.Equal("10-29-2026", cut.Find("#edit-due").GetAttribute("value"));   // the default Medium target at intake, MM-DD-YYYY

        cut.Find("[data-testid=edit-priority-Urgent]").Change(true);
        cut.Find("#edit-category").Change("Records");
        cut.Find("#edit-due").Change("11-02-2026");
        SubmitEdit(cut);

        cut.WaitForAssertion(() => Assert.Equal("Changes saved", Announcer.Message));
        var request = CaseService.Updates.Last();
        Assert.Equal(new CaseTriage(CasePriority.Urgent, CaseCategory.Records, null, new DateOnly(2026, 11, 2)), request.Triage);
        Assert.Contains("Urgent", cut.Find("[data-testid=summary-priority]").TextContent);
        Assert.Contains("Due in 14 days", cut.Find("[data-testid=summary-due]").TextContent);
    }

    [Test]
    public async Task A_server_error_on_a_triage_field_is_shown_on_the_field_and_in_the_summary()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Bad due date");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=edit-case]").Click();
        CaseService.NextUpdateError = new Error(ErrorKind.Validation, "Validation", "Some fields need attention.",
            new Dictionary<string, string[]> { [CaseFieldNames.DueOn] = ["Due date must be between 2000-01-01 and 2100-12-31."] });

        SubmitEdit(cut);

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#edit-due").GetAttribute("aria-invalid")));
        Assert.Contains("edit-due-error", cut.Find("#edit-due").GetAttribute("aria-describedby"));
        Assert.Equal("Due date must be between 2000-01-01 and 2100-12-31.", cut.Find("#edit-due-error").TextContent.Trim());
        Assert.Equal("#edit-due", cut.Find("#error-summary a").GetAttribute("href"));
    }

    // ---------- Assignment (supervisors assign; case workers take or hand back) ----------

    [Test]
    public async Task Supervisor_assigns_from_a_dialog_and_the_change_is_announced()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Assign from the dialog");
        var cut = RenderCase(created.Id);

        var assign = cut.WaitForElement("[data-testid=assign-case]");
        Assert.Contains("Assign", assign.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=take-case]"));
        assign.Click();
        cut.WaitForAssertion(() => Assert.Equal(["", "caseworker@example.test", "supervisor@example.test"],
            cut.FindAll("[data-testid=assign-owner] option").Select(o => o.GetAttribute("value"))));
        cut.Find("[data-testid=assign-owner]").Change("caseworker@example.test");
        cut.Find("[data-testid=assign-confirm]").Click();

        cut.WaitForAssertion(() => Assert.Equal("caseworker@example.test", cut.Find("[data-testid=summary-owner]").TextContent.Trim()));
        Assert.Equal("The case is assigned to caseworker@example.test", Announcer.Message);
        Assert.Contains("Reassign", cut.Find("[data-testid=assign-case]").TextContent);
        Assert.Contains("Assigned", cut.Find("[data-testid=audit-list]").TextContent);
    }

    [Test]
    public async Task Case_worker_takes_an_unassigned_case_then_can_hand_it_back()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Take this one", CaseStatus.Triaged);
        var cut = RenderCase(created.Id);

        Assert.Empty(cut.WaitForElement("[data-testid=detail-commands]").QuerySelectorAll("[data-testid=assign-case]"));
        cut.Find("[data-testid=take-case]").Click();

        cut.WaitForAssertion(() => Assert.Equal("caseworker@example.test", cut.Find("[data-testid=summary-owner]").TextContent.Trim()));
        Assert.Equal("The case is assigned to you", Announcer.Message);
        cut.Find("[data-testid=hand-back]").Click();
        cut.WaitForAssertion(() => Assert.Equal("Unassigned", cut.Find("[data-testid=summary-owner]").TextContent.Trim()));
        Assert.Equal("The case is now unassigned", Announcer.Message);
    }

    [TestCase(AppRoles.CaseWorker, "supervisor@example.test")]   // someone else's case
    [TestCase(AppRoles.IntakeClerk, null)]
    [TestCase(AppRoles.Auditor, null)]
    [TestCase(AppRoles.QualityAnalyst, null)]
    public async Task No_assignment_control_without_the_right(string role, string? owner)
    {
        SignInAs(role);
        var created = await SeedCaseAsync("Not mine to assign", triage: owner is null ? null : CaseTriage.Default with { AssignedTo = owner });
        var cut = RenderCase(created.Id);

        cut.WaitForElement("[data-testid=detail-commands]");
        Assert.Empty(cut.FindAll("[data-testid=assign-case], [data-testid=take-case], [data-testid=hand-back]"));
    }

    [Test]
    public async Task Closed_cases_offer_no_assignment()
    {
        SignInAs(AppRoles.Supervisor);
        var created = await SeedCaseAsync("Closed already", CaseStatus.Closed);
        var cut = RenderCase(created.Id);

        cut.WaitForElement("[data-testid=detail-commands]");
        Assert.Empty(cut.FindAll("[data-testid=assign-case]"));
    }

    [Test]
    public async Task Audit_trail_is_paged_newest_first()
    {
        var busy = await SeedCaseAsync("Edited many times");   // 1 entry (Created)
        CurrentUser.Actor = FakeCurrentUser.Authenticated("seed", "seed", AppRoles.Supervisor, FakeCurrentUser.AllPermissions);
        var version = busy.Version;
        for (var i = 0; i < 11; i++)   // 11 more (Edited)
        {
            version = (await CaseService.UpdateAsync(busy.Id, new UpdateCaseRequest($"Edited many times {i}", null, version), CancellationToken.None)).Value!.Version;
        }
        SignInAs(AppRoles.Supervisor);

        var cut = RenderCase(busy.Id);

        cut.WaitForAssertion(() => Assert.Equal("Showing 1–10 of 12 entries", cut.Find("[data-testid=audit-page-info]").TextContent));
        Assert.Equal(10, cut.FindAll("[data-testid=audit-list] li").Count);
        Assert.True(cut.Find("[data-testid=audit-previous]").HasAttribute("disabled"));
        cut.Find("[data-testid=audit-next]").Click();
        Assert.Equal("Showing 11–12 of 12 entries", cut.Find("[data-testid=audit-page-info]").TextContent);
        Assert.Contains("Created", cut.FindAll("[data-testid=audit-list] li").Last().TextContent);
    }

    // ---------- Attachments: remove the chosen file (#21) ----------

    [Test]
    public async Task A_chosen_file_can_be_removed_before_upload()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Remove before upload");
        var cut = RenderCase(created.Id);
        cut.WaitForElement("[data-testid=upload-form]");
        Assert.Empty(cut.FindAll("[data-testid=selected-file]"));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("draft", "wrong.txt", contentType: "text/plain"));
        var selected = cut.Find("[data-testid=selected-file]");
        Assert.Contains("wrong.txt", selected.TextContent);
        Assert.Contains("Remove wrong.txt", cut.Find("[data-testid=remove-selected-file]").TextContent.Replace("  ", " ", StringComparison.Ordinal).Trim());

        cut.Find("[data-testid=remove-selected-file]").Click();

        Assert.Empty(cut.FindAll("[data-testid=selected-file]"));
        Assert.Equal("wrong.txt removed. No file is selected.", Announcer.Message);
        cut.Find("[data-testid=upload-form]").Submit();
        Assert.Contains("Choose a file to upload first.", cut.Find("[data-testid=upload-error]").TextContent);   // nothing was kept
    }

    // ---------- Intake triage fields (#22) ----------

    [Test]
    public void Intake_offers_priority_category_owner_and_explains_the_default_due_date()
    {
        SignInAs(AppRoles.Supervisor);   // choosing the owner at intake is a supervisor's assignment
        var cut = Render<NewCase>();

        Assert.Contains("Leave empty to use the target for Medium priority: 10 days (10-29-2026).", cut.Find("#intake-due-hint").TextContent);
        cut.Find("[data-testid=intake-priority-Urgent]").Change(true);
        Assert.Contains("Urgent priority: 2 days (10-21-2026)", cut.Find("#intake-due-hint").TextContent);
        Assert.Equal("radio", cut.Find("#intake-priority").GetAttribute("type"));   // the first option, the error summary target
        Assert.Equal(3, cut.FindAll("#intake-owner option").Count);   // Unassigned and the two people who work cases

        cut.Find("#intake-title").Input("Triage at intake");
        cut.Find("#intake-category").Change("Travel");
        cut.Find("#intake-owner").Change("caseworker@example.test");
        cut.Find("[data-testid=intake-form]").Submit();
        cut.Find("[data-testid=confirm-intake-submit]").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/cases/1", Navigation.Uri, StringComparison.Ordinal));
    }

    [TestCase(AppRoles.IntakeClerk)]
    [TestCase(AppRoles.CaseWorker)]
    public void Intake_shows_the_owner_read_only_to_roles_that_do_not_assign(string role)
    {
        SignInAs(role);
        var cut = Render<NewCase>();

        var owner = cut.Find("#intake-owner");
        Assert.Equal("input", owner.TagName, ignoreCase: true);
        Assert.True(owner.HasAttribute("readonly"));
        Assert.Equal("Unassigned", owner.GetAttribute("value"));
        Assert.Contains("A supervisor assigns the owner.", cut.Find("#intake-owner-hint").TextContent);
    }

    // ---------- Notifications and badge (#21) ----------

    private void AddNotification(long id, bool unread, string action = AuditActions.StatusChanged, int minutesAgo = 5) =>
        NotificationFeed.Items.Add(new CaseNotificationDto(id, id, $"Case title {id}", action, CaseStatus.New, CaseStatus.Triaged,
            "supervisor@example.test", Time.GetUtcNow().AddMinutes(-minutesAgo), null, unread, AssignedToMe: id == 1));

    [Test]
    public void An_assignment_to_you_reads_as_such_and_is_labelled()
    {
        SignInAs(AppRoles.CaseWorker);
        NotificationFeed.Items.Add(new CaseNotificationDto(9, 9, "Assigned case", AuditActions.Assigned, null, null,
            "supervisor@example.test", Time.GetUtcNow(), "Assigned to caseworker@example.test", Unread: true, AssignedToMe: true, AssignedToYou: true));

        var cut = Render<Notifications>();

        var item = cut.WaitForElement("[data-testid=notifications-list] > li");
        Assert.Contains("supervisor@example.test assigned this case to you.", item.TextContent);
        Assert.Equal("Assigned to you", item.QuerySelector("[data-testid=assigned-to-you]")!.TextContent);
    }

    [Test]
    public void Notifications_page_marks_unread_entries_and_mark_all_as_read_clears_them()
    {
        SignInAs(AppRoles.CaseWorker);
        AddNotification(1, unread: true, minutesAgo: 1);
        AddNotification(2, unread: true, AuditActions.Created, minutesAgo: 2);
        AddNotification(3, unread: false, minutesAgo: 60);

        var cut = Render<Notifications>();

        cut.WaitForAssertion(() => Assert.Equal("2 unread notifications.", cut.Find("[data-testid=unread-summary]").TextContent.Trim()));
        var items = cut.FindAll("[data-testid=notifications-list] > li");
        Assert.Equal(["true", "true", "false"], items.Select(i => i.GetAttribute("data-unread")));
        Assert.Contains("New", items[0].QuerySelector(".badge--new")!.TextContent);
        Assert.Contains("supervisor@example.test moved it from New to Triaged.", items[0].TextContent);
        Assert.Contains("Your case", items[0].TextContent);
        Assert.Equal("cases/1?tab=history", items[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("supervisor@example.test created this case.", items[1].TextContent);

        cut.Find("[data-testid=mark-all-read]").Click();

        cut.WaitForAssertion(() => Assert.Equal("All caught up: no unread notifications.", cut.Find("[data-testid=unread-summary]").TextContent.Trim()));
        Assert.Equal("All notifications marked as read", Announcer.Message);
        Assert.All(cut.FindAll("[data-testid=notifications-list] > li"), i => Assert.Equal("false", i.GetAttribute("data-unread")));
        Assert.True(cut.Find("[data-testid=mark-all-read]").HasAttribute("disabled"));
    }

    [Test]
    public void Navigation_badge_shows_the_unread_count_and_updates_on_case_activity()
    {
        SignInAs(AppRoles.CaseWorker);
        AddNotification(1, unread: true);
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<h1>Body</h1>"))));

        cut.WaitForAssertion(() => Assert.Equal("1 unread", cut.Find("[data-testid=nav-unread]").TextContent));
        Assert.Contains("1 unread", cut.Find("nav#app-nav a[href=notifications]").TextContent);
        Assert.Equal("1 unread", cut.Find("[data-testid=bottom-unread]").TextContent);

        AddNotification(2, unread: true);
        Services.GetRequiredService<CaseActivitySignal>().Raise();

        cut.WaitForAssertion(() => Assert.Equal("2 unread", cut.Find("[data-testid=nav-unread]").TextContent));

        NotificationFeed.Items.Clear();
        Services.GetRequiredService<CaseActivitySignal>().Raise();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=nav-unread]")));
    }

    // ---------- Progress bar (#21) ----------

    [Test]
    public void Top_progress_bar_shows_while_any_page_work_runs()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<h1>Body</h1>"))));
        var activity = Services.GetRequiredService<PageActivity>();
        var bar = () => cut.Find("[data-testid=top-progress]");
        Assert.Equal("false", bar().GetAttribute("data-busy"));
        Assert.Equal("true", bar().GetAttribute("aria-hidden"));

        var first = activity.Begin();
        var second = activity.Begin();
        cut.WaitForAssertion(() => Assert.Equal("true", bar().GetAttribute("data-busy")));
        first.Dispose();
        first.Dispose();   // idempotent
        Assert.Equal("true", bar().GetAttribute("data-busy"));
        second.Dispose();
        cut.WaitForAssertion(() => Assert.Equal("false", bar().GetAttribute("data-busy")));
    }
}
