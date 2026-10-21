using CaseIntake.Contracts.Diagnostics;
using CaseIntake.Contracts.Security;
using CaseIntake.TestKit.Fakes;
using CaseIntake.Web.Components.Pages.Admin;

namespace CaseIntake.Web.Tests;

public class ErrorAndHealthPageTests : WebTestContext
{
    public ErrorAndHealthPageTests()
    {
        SignInAs(AppRoles.Administrator);
        ErrorLog.Errors.Add(FakeErrorLogService.Error(5));
        ErrorLog.Errors.Add(FakeErrorLogService.Error(6, ErrorStatus.Resolved, ErrorLevel.Critical));
    }

    [Test]
    public void Lists_errors_with_level_status_and_message_and_filters_by_status_from_the_url()
    {
        var all = Render<Errors>();
        Assert.Equal(2, all.FindAll("[data-testid=error-row]").Count);
        Assert.Equal("admin/errors/5", all.Find("[data-testid=error-row] a").GetAttribute("href"));
        Assert.Contains("Saving case 12 failed", all.Find("[data-testid=error-row]").TextContent);

        Navigation.NavigateTo("admin/errors?status=Open");
        var open = Render<Errors>();
        Assert.Single(open.FindAll("[data-testid=error-row]"));
        Assert.Equal(ErrorStatus.Open, Assert.IsType<ErrorQuery>(ErrorLog.Requests[^1]).Status);
    }

    [Test]
    public void An_error_shows_its_reference_a_focusable_stack_trace_and_a_link_to_the_same_errors()
    {
        var cut = Render<ErrorDetail>(p => p.Add(x => x.Id, 5));

        Assert.Equal("Error 5", cut.Find("h1").TextContent.Trim());
        Assert.Equal("op-err", cut.Find("[data-testid=error-reference]").TextContent);
        var stack = cut.Find("[data-testid=error-stack]");
        Assert.Equal("0", stack.GetAttribute("tabindex"));
        Assert.Equal("stack-heading", stack.GetAttribute("aria-labelledby"));
        Assert.Equal("admin/errors?same=0123456789abcdef", cut.Find("[data-testid=error-occurrences]").GetAttribute("href"));
        Assert.Contains("3 times", cut.Find("[data-testid=error-occurrences]").TextContent);
    }

    [Test]
    public void Changing_the_status_sends_the_version_and_note_and_shows_the_result()
    {
        var cut = Render<ErrorDetail>(p => p.Add(x => x.Id, 5));
        cut.Find("[data-testid=error-status-Resolved]").Change(true);
        cut.Find("[data-testid=error-note]").Change("Fixed by restarting the lookup mock.");

        cut.Find("[data-testid=error-status-form]").Submit();

        Assert.Equal(new SetErrorStatusRequest(ErrorStatus.Resolved, "Fixed by restarting the lookup mock.", 1), ErrorLog.Requests[^1]);
        Assert.Equal("Resolved", cut.Find("[data-testid=error-status-value]").TextContent);
        Assert.Equal("Status saved: Resolved.", Announcer.Message);
        Assert.Equal("New status", cut.Find("[data-testid=error-status-form] legend").TextContent);
    }

    [Test]
    public void Without_errors_manage_the_status_is_read_only()
    {
        CurrentUser.Actor = FakeCurrentUser.Authenticated("id-v", "viewer", "Viewer", [Permissions.ErrorsView]);
        Auth.SetPolicies(Permissions.ErrorsView);

        var cut = Render<ErrorDetail>(p => p.Add(x => x.Id, 5));

        Assert.Empty(cut.FindAll("[data-testid=error-status-form]"));
        Assert.Equal("Open", cut.Find("[data-testid=error-status-value]").TextContent);
    }

    [Test]
    public void System_health_states_every_result_in_text_and_checks_again_on_request()
    {
        var cut = Render<SystemHealth>();

        Assert.Contains("Overall: Degraded.", cut.Find("[data-testid=health-overall]").TextContent);
        var rows = cut.FindAll("[data-testid=health-checks] tbody tr");
        Assert.Contains("Healthy", rows[0].TextContent);
        Assert.Contains("Degraded", rows[1].TextContent);
        Assert.Contains("Slow to answer", rows[1].TextContent);
        Assert.Contains("8 applied; none pending", cut.Find("[data-testid=health-migrations]").TextContent);
        Assert.Contains("Sign in failed", cut.Find("[data-testid=health-counts]").TextContent);

        HealthService.Next = HealthService.Next with { Overall = HealthState.Healthy };
        cut.Find("[data-testid=check-again]").Click();

        Assert.Equal(2, HealthService.Calls);
        Assert.Equal("Checked again. Overall: Healthy.", Announcer.Message);
    }

    [Test]
    public void Administrators_see_errors_and_system_health_in_the_navigation()
    {
        var groups = Components.Layout.AppNavigation.Groups.SelectMany(g => g.Items).ToList();

        Assert.Contains(groups, i => i.Href == "admin/errors" && i.Policy == Permissions.ErrorsView);
        Assert.Contains(groups, i => i.Href == "admin/health" && i.Policy == Permissions.SystemHealth);
    }
}
