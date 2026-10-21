using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using Error = CaseIntake.Contracts.Common.Error;
using ErrorPage = CaseIntake.Web.Components.Pages.Error;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Account.Pages;
using CaseIntake.Web.Components.Kit;
using CaseIntake.Web.Components.Layout;
using CaseIntake.Web.Components.Pages;
using CaseIntake.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CaseIntake.Web.Tests;

/// <summary>
/// My tasks, reports, notifications and dashboard edge states, the not-found, error, welcome, access-denied and More
/// pages, the phone sheets, focus after navigation and the unread badge.
/// </summary>
[Category("Story:E5-S1")]
[Category("Story:Dashboard")]
[Category("Req:R-4.2-11")]
[Category("Req:R-4.2-12")]
public class PagesAndShellTests : WebTestContext
{
    private static readonly Error Unavailable = new(ErrorKind.ExternalUnavailable, "Down", "The case service is unavailable.");

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<h1>Body</h1>"))));

    // ---- My tasks ----

    [Test]
    public async Task My_tasks_lists_cases_waiting_for_the_roles_transitions_with_the_next_step()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedCaseAsync("Still new");
        var triaged = await SeedCaseAsync("Ready to start", CaseStatus.Triaged);
        await SeedCaseAsync("Being worked", CaseStatus.InProgress);

        var cut = Render<MyWork>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=mywork-list] > li").Count));
        var query = CaseService.ListQueries.Last();
        Assert.Equal([CaseStatus.Triaged, CaseStatus.InProgress], query.Statuses!.Order());
        Assert.Equal(CaseSortField.UpdatedAt, query.SortBy);
        Assert.Equal(SortDirection.Ascending, query.Direction);
        var list = cut.Find("[data-testid=mywork-list]").TextContent;
        var nextSteps = cut.FindAll("[data-testid=mywork-next]").Select(a => a.TextContent).ToList();
        Assert.Contains(nextSteps, t => t.Contains("Move to In Progress", StringComparison.Ordinal));
        Assert.Contains(nextSteps, t => t.Contains("Move to Resolved", StringComparison.Ordinal));
        Assert.DoesNotContain("Still new", list);
        Assert.Contains(cut.FindAll("[data-testid=mywork-list] a"), a => a.GetAttribute("href") == $"cases/{triaged.Id}");
        Assert.Equal("2 cases are waiting for you. Page 1 of 1.", cut.Find("[role=status]").TextContent);
        Assert.True(cut.Find("[aria-label='Go to previous page']").HasAttribute("disabled"));
        Assert.True(cut.Find("[aria-label='Go to next page']").HasAttribute("disabled"));
    }

    [Test]
    public async Task My_tasks_pages_with_buttons_that_keep_the_url()
    {
        SignInAs(AppRoles.Supervisor);
        for (var i = 0; i < 27; i++)
        {
            await SeedCaseAsync($"Needs triage {i:00}");
        }
        Navigation.NavigateTo("my-work?status=New&page=2");

        var cut = Render<MyWork>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=mywork-list] > li").Count));
        Assert.Equal(2, CaseService.ListQueries.Last().Page);
        Assert.Equal("Showing 26–27 of 27 cases", cut.Find("[data-testid=mywork-pager-range]").TextContent);
        Assert.Equal("Page 2 of 2", cut.Find("[data-testid=mywork-pager-info]").TextContent);
        Assert.True(cut.Find("[aria-label='Go to next page']").HasAttribute("disabled"));
        Assert.All(cut.FindAll("[data-testid=mywork-next]"), a => Assert.Contains("Move to Triaged", a.TextContent));

        var previous = cut.Find("[aria-label='Go to previous page']");
        Assert.False(previous.HasAttribute("disabled"));
        previous.Click();

        // Going back to the first page drops page= and keeps the status tab.
        Assert.EndsWith("my-work?status=New", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal(25, cut.FindAll("[data-testid=mywork-list] > li").Count));
    }

    [Test]
    public void My_tasks_without_waiting_cases_says_all_caught_up()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = Render<MyWork>();
        cut.WaitForAssertion(() => Assert.Contains("You're all caught up", cut.Find("[data-testid=mywork-empty]").TextContent));
        Assert.Equal("0 cases are waiting for you. Page 1 of 1.", cut.Find("[role=status]").TextContent);
    }

    [Test]
    public void Roles_without_transitions_have_no_tasks_and_the_service_is_not_asked()
    {
        SignInAs(AppRoles.Auditor);
        var cut = Render<MyWork>();
        cut.WaitForAssertion(() => Assert.Contains("No tasks for your role", cut.Find("[data-testid=mywork-none]").TextContent));
        Assert.Empty(CaseService.ListQueries);
    }

    [Test]
    public void My_tasks_shows_service_errors_and_loading()
    {
        SignInAs(AppRoles.CaseWorker);
        CaseService.ListError = Unavailable;
        var failed = Render<MyWork>();
        failed.WaitForAssertion(() => Assert.Contains("The case service is unavailable.", failed.Find("[data-testid=mywork-error]").TextContent));
        Assert.Empty(failed.FindAll("[data-testid=mywork-list]"));

        CaseService.ListError = null;
        CaseService.ListGate = new TaskCompletionSource();
        var loading = Render<MyWork>();
        Assert.Contains("Loading your tasks…", loading.Find("[data-testid=loading]").TextContent);
        CaseService.ListGate.SetResult();
        loading.WaitForElement("[data-testid=mywork-empty]");
    }

    // ---- Reports ----

    [Test]
    public async Task Reports_show_cases_by_status_with_shares_and_by_month()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("New one");
        await SeedCaseAsync("New two");
        await SeedCaseAsync("Triaged", CaseStatus.Triaged);
        await SeedCaseAsync("Closed", CaseStatus.Closed);

        var cut = Render<Reports>();

        var byStatus = cut.WaitForElement("[aria-labelledby=by-status] table");
        var rows = byStatus.QuerySelectorAll("tbody tr").Select(r => r.QuerySelectorAll("th, td").Select(c => c.TextContent.Trim()).ToList()).ToList();
        // Columns: status (a link to the filtered list), share, cases.
        Assert.Equal(["New: open the list", "50%", "2"], rows[0]);
        Assert.Equal(["Triaged: open the list", "25%", "1"], rows[1]);
        Assert.Equal(["In Progress: open the list", "0%", "0"], rows[2]);
        Assert.Equal(["Closed: open the list", "25%", "1"], rows[4]);
        Assert.Equal("cases?status=New", byStatus.QuerySelector("tbody tr th a")!.GetAttribute("href"));
        Assert.Contains("4", byStatus.QuerySelector("tfoot")!.TextContent);
        var months = cut.FindAll("[aria-labelledby=by-month] tbody tr");
        Assert.Equal(12, months.Count);
        Assert.Equal(["Oct 2026", "", "4", "1"], months[0].QuerySelectorAll("th, td").Select(c => c.TextContent.Trim()));
        Assert.Equal("exports/cases.csv", cut.Find("[data-testid=report-export]").GetAttribute("href"));
    }

    [Test]
    public void Reports_with_no_cases_show_zero_shares_and_errors_are_alerts()
    {
        SignInAs(AppRoles.CaseWorker);
        var empty = Render<Reports>();
        Assert.All(empty.WaitForElement("[aria-labelledby=by-status] table").QuerySelectorAll("tbody tr td:nth-of-type(1)"), td => Assert.Equal("0%", td.TextContent.Trim()));

        CaseService.ListError = Unavailable;
        var failed = Render<Reports>();
        failed.WaitForAssertion(() => Assert.Contains("The case service is unavailable.", failed.Find("[data-testid=reports-error]").TextContent));
        Assert.Empty(failed.FindAll("[aria-labelledby=by-status]"));
        Assert.Empty(failed.FindAll("[data-testid=loading]"));

        CaseService.ListError = null;
        CaseService.ListGate = new TaskCompletionSource();
        var loading = Render<Reports>();
        Assert.Contains("Loading reports…", loading.Find("[data-testid=loading]").TextContent);
        CaseService.ListGate.SetResult();
        loading.WaitForElement("[aria-labelledby=by-status]");
    }

    // ---- Dashboard edge states ----

    [Test]
    public async Task Dashboard_shows_skeletons_while_loading_then_says_when_figures_are_partial()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("First case");
        await SeedCaseAsync("Second case");
        Services.GetRequiredService<IOptions<DashboardOptions>>().Value.MaxCasesScanned = 1;
        CaseService.ListGate = new TaskCompletionSource();

        var cut = Render<Home>();

        Assert.Contains("Loading dashboard…", cut.Find("[data-testid=loading]").TextContent);
        Assert.Equal(7, cut.FindAll(".skeleton--kpi").Count);
        CaseService.ListGate.SetResult();
        cut.WaitForAssertion(() => Assert.Contains("Figures are based on the first 1 of 2 cases", cut.Find("[role=note]").TextContent));
        Assert.Equal("2", cut.Find("[data-testid=kpi-total] .kpi__value").TextContent);
    }

    [Test]
    public void Dashboard_hides_unexpected_failures_behind_a_generic_message()
    {
        SignInAs(AppRoles.CaseWorker);
        CaseService.ListException = new InvalidOperationException("connection string leaked");

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("Something went wrong while loading the dashboard. Try again.", cut.Find("[data-testid=dashboard-error]").TextContent));
        Assert.DoesNotContain("connection string", cut.Markup);
    }

    // ---- Notifications ----

    private void AddNotification(long id, string action, bool unread = false, CaseStatus? from = null, CaseStatus? to = null, string? details = null, bool assignedToYou = false) =>
        NotificationFeed.Items.Add(new CaseNotificationDto(id, id, $"Case title {id}", action, from, to, "supervisor@example.test",
            Time.GetUtcNow().AddMinutes(-id), details, unread, AssignedToMe: false, AssignedToYou: assignedToYou));

    [Test]
    public void Notifications_describe_every_kind_of_activity()
    {
        SignInAs(AppRoles.CaseWorker);
        AddNotification(1, AuditActions.Reopened, to: CaseStatus.InProgress);
        AddNotification(2, AuditActions.AttachmentUploaded);
        AddNotification(3, AuditActions.Edited, details: "Changed: Title.");
        AddNotification(4, AuditActions.Assigned, details: "Assigned to caseworker2@example.test");
        AddNotification(5, "Archived");
        AddNotification(6, AuditActions.StatusChanged, to: CaseStatus.Triaged);

        var cut = Render<Notifications>();

        var texts = cut.WaitForElement("[data-testid=notifications-list]").QuerySelectorAll(".notification__text").Select(p => p.TextContent).ToList();
        Assert.Equal(
        [
            "supervisor@example.test reopened it (In Progress).",
            "supervisor@example.test attached a file.",
            "supervisor@example.test edited it. Changed: Title.",
            "supervisor@example.test: Assigned to caseworker2@example.test.",
            "supervisor@example.test: Archived.",
            "supervisor@example.test: StatusChanged.",
        ], texts);
        Assert.Equal("All caught up: no unread notifications.", cut.Find("[data-testid=unread-summary]").TextContent.Trim());
        Assert.Contains("newest first", cut.Find(".page-header__subtitle").TextContent);
    }

    [Test]
    public void One_unread_notification_is_singular_and_mark_all_read_updates_the_subtitle()
    {
        SignInAs(AppRoles.CaseWorker);
        AddNotification(1, AuditActions.Created, unread: true);
        var cut = Render<Notifications>();
        cut.WaitForAssertion(() => Assert.Equal("1 unread notification.", cut.Find("[data-testid=unread-summary]").TextContent.Trim()));

        cut.Find("[data-testid=mark-all-read]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Last marked as read 10-19-2026 08:00 ET.", cut.Find(".page-header__subtitle").TextContent));
        Assert.Equal(1, NotificationFeed.MarkAllReadCalls);
    }

    [Test]
    public void Notifications_show_empty_failed_and_refreshed_states()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = Render<Notifications>();
        cut.WaitForAssertion(() => Assert.Contains("You're all caught up", cut.Find("[data-testid=notifications-empty]").TextContent));
        Assert.True(cut.Find("[data-testid=mark-all-read]").HasAttribute("disabled"));

        NotificationScript.FeedError = new Error(ErrorKind.ExternalUnavailable, "Down", "Notifications are unavailable.");
        cut.Find("[data-testid=notifications-refresh]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Notifications are unavailable.", cut.Find("[data-testid=notifications-error]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=loading]"));

        NotificationScript.FeedError = null;
        NotificationScript.FeedException = new InvalidOperationException("boom");
        Services.GetRequiredService<CaseActivitySignal>().Raise();
        cut.WaitForAssertion(() => Assert.Contains("The notifications could not be loaded. Try again.", cut.Find("[data-testid=notifications-error]").TextContent));

        NotificationScript.FeedException = null;
        AddNotification(1, AuditActions.Created, unread: true);
        cut.Find("[data-testid=notifications-refresh]").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=notifications-list] > li")));
        Assert.Empty(cut.Find("[data-testid=notifications-error]").TextContent.Trim());
    }

    [Test]
    public void A_failed_mark_all_as_read_keeps_the_unread_entries()
    {
        SignInAs(AppRoles.CaseWorker);
        AddNotification(1, AuditActions.Created, unread: true);
        NotificationScript.MarkAllReadError = new Error(ErrorKind.ExternalUnavailable, "Down", "Could not save the read marker.");
        var cut = Render<Notifications>();

        cut.WaitForElement("[data-testid=mark-all-read]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Could not save the read marker.", cut.Find("[data-testid=notifications-error]").TextContent));
        Assert.Equal("true", cut.Find("[data-testid=notifications-list] > li").GetAttribute("data-unread"));
        Assert.Null(Announcer.Message);
    }

    // ---- Unread badge ----

    [Test]
    public void Badge_caps_large_counts_hides_on_failure_and_refreshes_after_navigation()
    {
        SignInAs(AppRoles.CaseWorker);
        for (var i = 1; i <= 120; i++)
        {
            AddNotification(i, AuditActions.Created, unread: true);
        }
        var cut = Render<NotificationBadge>(p => p.Add(b => b.TestId, "badge"));
        cut.WaitForAssertion(() => Assert.Equal("99+ unread", cut.Find("[data-testid=badge]").TextContent));

        NotificationFeed.Items.RemoveRange(3, 117);
        var calls = NotificationScript.CountCalls;
        Navigation.NavigateTo("cases");
        cut.WaitForAssertion(() => Assert.Equal("3 unread", cut.Find("[data-testid=badge]").TextContent));
        Assert.True(NotificationScript.CountCalls > calls);

        NotificationScript.CountException = new InvalidOperationException("boom");
        Services.GetRequiredService<CaseActivitySignal>().Raise();
        Navigation.NavigateTo("my-work");
        // A badge failure never breaks the page: the last known count stays.
        Assert.Equal("3 unread", cut.Find("[data-testid=badge]").TextContent);

        cut.Instance.Dispose();
        var after = NotificationScript.CountCalls;
        Services.GetRequiredService<CaseActivitySignal>().Raise();
        Assert.Equal(after, NotificationScript.CountCalls);
    }

    // ---- Phone sheets ----

    [Test]
    public void Profile_sheet_opens_from_the_app_bar_with_the_user_role_and_sign_out()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = RenderLayout();

        cut.Find("[data-testid=mobile-profile]").Click();

        InteropModule.VerifyInvoke("showDialog");
        var sheet = cut.Find("[data-testid=profile-sheet]");
        Assert.Contains("caseworker@example.test", sheet.TextContent);
        Assert.Contains("Role: Case worker", sheet.TextContent);
        Assert.Equal("CA", sheet.QuerySelector(".avatar")!.TextContent);
        Assert.Equal("account/logout", sheet.QuerySelector("form")!.GetAttribute("action"));

        sheet.QuerySelector("[aria-label=Close]")!.Click();
        InteropModule.VerifyInvoke("closeDialog");
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Test]
    public void More_sheet_lists_the_permitted_pages_and_closes_when_one_is_chosen()
    {
        SignInAs(AppRoles.CaseWorker);
        Navigation.NavigateTo("cases");
        var cut = RenderLayout();

        cut.Find("[data-testid=bottom-nav-more]").Click();

        var sheet = cut.Find("[data-testid=more-sheet]");
        var links = sheet.QuerySelectorAll("nav[aria-label='All pages'] a");
        Assert.Contains(links, a => a.GetAttribute("href") == "cases" && a.GetAttribute("aria-current") == "page");
        Assert.DoesNotContain(links, a => a.GetAttribute("href") == "admin/users");
        Assert.Single(links, a => a.GetAttribute("aria-current") == "page");

        links.First(a => a.GetAttribute("href") == "notifications").Click();

        InteropModule.VerifyInvoke("closeDialog");
        Assert.Empty(cut.Find("[data-testid=more-sheet]").QuerySelectorAll("nav"));
    }

    [Test]
    public void More_page_shows_the_profile_and_the_site_map()
    {
        SignInAs(AppRoles.Supervisor);
        Navigation.NavigateTo("more");

        var cut = Render<More>();

        Assert.Contains("supervisor@example.test", cut.Find(".user-status__name").TextContent);
        Assert.Equal("Role: Supervisor", cut.Find(".user-status__roles").TextContent);
        Assert.Equal("SU", cut.Find(".avatar").TextContent);
        Assert.Contains(cut.FindAll("nav[aria-label='All pages'] a"), a => a.GetAttribute("href") == "admin/users");
        Assert.NotEmpty(cut.FindAll("p.sheet-nav__group"));
    }

    // ---- Focus after navigation ----

    [Test]
    public void Focus_moves_to_the_heading_only_after_navigating_to_a_different_page()
    {
        RouteData Route(Type page, long? id = null) => new(page, id is null ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["Id"] = id });
        var cut = Render<FocusOnNavigation>(p => p.Add(f => f.RouteData, Route(typeof(Home))));
        Assert.DoesNotContain(InteropModule.Invocations, i => i.Identifier == "focusSelector");

        cut.Render(p => p.Add(f => f.RouteData, Route(typeof(Home))));
        Assert.DoesNotContain(InteropModule.Invocations, i => i.Identifier == "focusSelector");

        cut.Render(p => p.Add(f => f.RouteData, Route(typeof(Components.Pages.Cases.CaseDetail), 4)).Add(f => f.Selector, "#page-title"));
        var focus = InteropModule.VerifyInvoke("focusSelector");
        Assert.Equal("#page-title", focus.Arguments[0]);

        cut.Render(p => p.Add(f => f.RouteData, Route(typeof(Components.Pages.Cases.CaseDetail), 5)));
        Assert.Equal(2, InteropModule.Invocations.Count(i => i.Identifier == "focusSelector"));
        Assert.Equal("CaseIntake.Web.Components.Pages.Cases.CaseDetail|Id=5", FocusOnNavigation.KeyFor(typeof(Components.Pages.Cases.CaseDetail), new Dictionary<string, object?> { ["Id"] = 5 }));
    }

    // ---- Not found, error, welcome and access denied pages ----

    [Test]
    public void Not_found_offers_ways_out_and_focuses_the_heading_after_in_app_navigation()
    {
        SignInAs(AppRoles.CaseWorker);
        var first = Render<NotFound>();
        Assert.Contains("doesn't exist", first.Find("[data-testid=not-found]").TextContent);
        Assert.Equal(["cases", ""], first.FindAll("[data-testid=not-found] a").Select(a => a.GetAttribute("href")));
        Assert.Equal("Page not found", first.Find("h1").TextContent);
        // The first page of a circuit leaves focus alone.
        Assert.DoesNotContain(InteropModule.Invocations, i => i.Identifier == "focusSelector");

        Navigation.NavigateTo("cases/999");
        Render<NotFound>();

        Assert.Equal("h1", InteropModule.VerifyInvoke("focusSelector").Arguments[0]);
        Assert.Equal("", Services.GetRequiredService<ShellState>().BackHref);
    }

    [Test]
    public void Error_page_shows_the_operation_reference_but_never_the_exception()
    {
        var context = new DefaultHttpContext();
        context.Response.Headers[ServiceDefaults.OperationContextMiddleware.HeaderName] = "op-1234";

        var cut = Render<ErrorPage>(p => p.AddCascadingValue<HttpContext>(context));

        Assert.Equal("Something went wrong", cut.Find("h1").TextContent);
        Assert.Equal("op-1234", cut.Find("code").TextContent);
        Assert.Equal("", cut.Find(".btn--primary").GetAttribute("href"));

        var withTrace = new DefaultHttpContext { TraceIdentifier = "trace-5" };
        Assert.Equal("trace-5", Render<ErrorPage>(p => p.AddCascadingValue<HttpContext>(withTrace)).Find("code").TextContent);
    }

    [Test]
    public void Welcome_offers_sign_in_to_visitors_and_the_dashboard_to_signed_in_users()
    {
        var visitor = Render<Welcome>(p => p.AddCascadingValue<HttpContext>(new DefaultHttpContext()));
        Assert.Equal("account/login", visitor.Find("[data-testid=landing-sign-in]").GetAttribute("href"));
        Assert.Equal(3, visitor.FindAll("[data-testid=landing-features] li").Count);
        Assert.Empty(visitor.FindAll("[data-testid=landing-dashboard]"));

        var signedIn = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "clerk")], "test")),
        };
        var user = Render<Welcome>(p => p.AddCascadingValue<HttpContext>(signedIn));
        Assert.Equal("", user.Find("[data-testid=landing-dashboard]").GetAttribute("href"));
        Assert.Empty(user.FindAll("[data-testid=landing-sign-in]"));
    }

    [Test]
    public void Access_denied_page_answers_403()
    {
        var context = new DefaultHttpContext();
        var cut = Render<AccessDenied>(p => p.AddCascadingValue<HttpContext>(context));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.NotEmpty(cut.FindAll("h1"));
    }

    // ---- Services ----

    [Test]
    public void Status_announcer_clears_only_when_there_is_a_message()
    {
        var announcer = new StatusAnnouncer();
        var changes = 0;
        announcer.Changed += () => changes++;

        announcer.Clear();
        Assert.Equal(0, changes);

        announcer.Announce("Saved");
        announcer.Announce("Saved");
        Assert.Equal(2, announcer.Sequence);
        announcer.Clear();
        Assert.Null(announcer.Message);
        Assert.Equal(3, changes);
        Assert.Throws<ArgumentException>(() => announcer.Announce(" "));
    }

    [Test]
    public async Task Status_announcer_outlet_clears_the_region_when_the_message_is_cleared()
    {
        var cut = Render<StatusAnnouncerOutlet>();
        Announcer.Announce("Case 1 created");
        cut.WaitForAssertion(() => Assert.Contains("Case 1 created", cut.Markup), TimeSpan.FromSeconds(3));

        await cut.InvokeAsync(Announcer.Clear);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Case 1 created", cut.Markup));
    }
}
