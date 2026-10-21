using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Security;
using CaseIntake.Web.Components.Pages.Admin;

namespace CaseIntake.Web.Tests;

public class AuditLogPageTests : WebTestContext
{
    public AuditLogPageTests()
    {
        SignInAs(AppRoles.Administrator);
        AuditLog.Entries.Add(FakeAuditLogService.Entry("A-1", AuditLogActions.UserPermissionsChanged, summary: "Overrides changed: Edit cases"));
        AuditLog.Entries.Add(FakeAuditLogService.Entry("A-2", AuditLogActions.SignInFailed, AuditCategories.Authentication, AuditOutcome.Failure,
            entityType: null, entityId: null, at: new DateTimeOffset(2026, 10, 21, 14, 0, 0, TimeSpan.Zero)));
        AuditLog.Entries.Add(FakeAuditLogService.Entry("C-9", "StatusChanged", AuditCategories.Cases, entityType: AuditEntityTypes.Case, entityId: "42",
            changes: [new AuditChangeDto("Status", "New", "Triaged")], at: new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void Lists_entries_newest_first_with_readable_actions_subjects_and_outcomes()
    {
        var cut = Render<AuditLog>();

        var rows = cut.FindAll("[data-testid=audit-row]");
        Assert.Equal(3, rows.Count);
        Assert.Contains("Sign in failed", rows[0].TextContent);
        Assert.Contains("Failed", rows[0].QuerySelector(".audit-outcome")!.TextContent);
        Assert.Contains("User permissions changed", rows[1].TextContent);
        Assert.Equal("admin/users/7", rows[1].QuerySelector("td:nth-child(4) a")!.GetAttribute("href"));
        Assert.Equal("cases/42", rows[2].QuerySelector("td:nth-child(4) a")!.GetAttribute("href"));
        Assert.Equal("admin/audit/A-2", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("When", cut.Find("[data-testid=audit-table] th").TextContent);
    }

    [Test]
    public void Query_string_filters_select_entries_and_reach_the_export_link()
    {
        Navigation.NavigateTo("admin/audit?category=Authentication&outcome=Failure&from=2026-10-01&to=2026-10-21");
        var cut = Render<AuditLog>();

        var query = AuditLog.Queries[^1];
        Assert.Equal(AuditCategories.Authentication, query.Category);
        Assert.Equal(AuditOutcome.Failure, query.Outcome);
        Assert.Equal(new DateOnly(2026, 10, 1), query.FromDate);
        Assert.Single(cut.FindAll("[data-testid=audit-row]"));
        Assert.Equal("exports/audit.csv?from=2026-10-01&to=2026-10-21&category=Authentication&outcome=Failure",
            cut.Find("[data-testid=export-audit]").GetAttribute("href"));
        Assert.Equal(AuditCategories.Authentication, cut.Find("#audit-category").GetAttribute("value"));
    }

    [Test]
    public void Applying_filters_navigates_to_the_filtered_url_and_every_filter_has_a_label()
    {
        var cut = Render<AuditLog>();
        cut.Find("#audit-category").Change(AuditCategories.Users);
        cut.Find("#audit-search").Change("permissions");

        cut.Find("[data-testid=audit-filters]").Submit();

        Assert.EndsWith("admin/audit?category=Users&q=permissions", Navigation.Uri);
        foreach (var input in cut.FindAll("[data-testid=audit-filters] input:not([aria-hidden]), [data-testid=audit-filters] select"))
        {
            Assert.NotNull(cut.Find($"label[for='{input.Id}']"));
        }
    }

    [Test]
    public void A_subject_filter_from_a_history_link_is_shown_as_a_removable_chip()
    {
        Navigation.NavigateTo("admin/audit?entityType=User&entityId=7");
        var cut = Render<AuditLog>();

        Assert.Contains("Subject: User 7", cut.Find("[data-testid=audit-subject-filter]").TextContent);
        Assert.Single(cut.FindAll("[data-testid=audit-row]"));
        Assert.Equal("Remove filter: subject", cut.Find("[data-testid=audit-subject-filter] button").GetAttribute("aria-label"));
    }

    [Test]
    public void An_entry_shows_its_fields_and_changes_with_a_link_to_its_operation()
    {
        var cut = Render<AuditEntry>(p => p.Add(x => x.Key, "C-9"));

        Assert.Equal("Status changed", cut.Find("h1").TextContent.Trim());
        var change = cut.Find("[data-testid=audit-changes] tbody tr");
        Assert.Equal("Status", change.QuerySelector("th")!.TextContent);
        Assert.Contains("New", change.TextContent);
        Assert.Contains("Triaged", change.TextContent);
        Assert.Equal("admin/audit?q=op-1", cut.Find("[data-testid=same-operation]").GetAttribute("href"));
        Assert.Equal("Succeeded", cut.Find("[data-testid=audit-entry-outcome]").TextContent);
    }

    [Test]
    public void An_unknown_entry_shows_not_found()
    {
        var cut = Render<AuditEntry>(p => p.Add(x => x.Key, "A-404"));

        Assert.Contains("Audit entry not found", cut.Find("[data-testid=audit-entry-not-found]").TextContent);
    }

    [Test]
    public void The_user_page_shows_that_users_history_with_a_link_to_all_of_it()
    {
        UserAdmin.Users.Add(FakeUserAdministrationService.Detail("7", "worker@example.test", [AppRoles.CaseWorker]));
        var cut = Render<UserDetail>(p => p.Add(x => x.Id, "7"));

        var history = cut.Find("[data-testid=audit-history]");
        Assert.Contains("User permissions changed", history.TextContent);
        Assert.DoesNotContain("Sign in failed", history.TextContent);
        Assert.Equal("admin/audit?entityType=User&entityId=7", cut.Find("[data-testid=history-all]").GetAttribute("href"));
        Assert.Equal(AuditEntityTypes.User, AuditLog.Queries[^1].EntityType);
    }

    [Test]
    public void History_is_hidden_without_audit_view()
    {
        SignInAs(AppRoles.IntakeClerk);
        CurrentUser.Actor = CaseIntake.TestKit.Fakes.FakeCurrentUser.Authenticated("id-x", "x", "Custom", [Permissions.UsersManage]);
        Auth.SetPolicies(Permissions.UsersManage);
        UserAdmin.Users.Add(FakeUserAdministrationService.Detail("7", "worker@example.test", [AppRoles.CaseWorker]));

        var cut = Render<UserDetail>(p => p.Add(x => x.Id, "7"));

        Assert.Empty(cut.FindAll("[data-testid=audit-history]"));
        Assert.Empty(AuditLog.Queries);
    }
}
