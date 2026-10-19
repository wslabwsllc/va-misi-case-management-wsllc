using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Kit.Charts;
using CaseIntake.Web.Components.Layout;
using CaseIntake.Web.Components.Pages;

namespace CaseIntake.Web.Tests;

/// <summary>Dashboard: KPIs and charts from the authorized case service only, per role; empty, error and retry states.</summary>
[Trait("Category", "Story:Dashboard")]
[Trait("Category", "Req:R-4.2-11")]
[Trait("Category", "Req:R-4.2-12")]
public class DashboardTests : WebTestContext
{
    private async Task SeedMixAsync()
    {
        await SeedCaseAsync("New case one");
        await SeedCaseAsync("New case two");
        await SeedCaseAsync("Triaged case", CaseStatus.Triaged);
        await SeedCaseAsync("In progress case", CaseStatus.InProgress);
        await SeedCaseAsync("Resolved case", CaseStatus.Resolved);
        await SeedCaseAsync("Closed case", CaseStatus.Closed);
    }

    private static string Kpi(IRenderedComponent<Home> cut, string id) => cut.Find($"[data-testid={id}] .kpi__value").TextContent;

    [Fact]
    public async Task Kpis_are_computed_from_the_case_service_data()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        Time.Advance(TimeSpan.FromDays(20));   // every open case is now older than the 14-day overdue threshold

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));
        Assert.Equal("4", Kpi(cut, "kpi-open"));
        Assert.Equal("1", Kpi(cut, "kpi-pending"));
        Assert.Equal("4", Kpi(cut, "kpi-overdue"));
        Assert.Equal("2", Kpi(cut, "kpi-resolved"));
        Assert.Equal("5", Kpi(cut, "kpi-mine"));   // Supervisor can act on New, Triaged, In Progress and Resolved
        Assert.All(CaseService.ListQueries, q => Assert.Equal(100, q.PageSize));
    }

    [Fact]
    public async Task Assigned_to_me_differs_by_role_because_it_follows_the_users_transition_permissions()
    {
        await SeedMixAsync();
        SignInAs(AppRoles.CaseWorker);
        var worker = Render<Home>();
        worker.WaitForAssertion(() => Assert.Equal("2", Kpi(worker, "kpi-mine")));   // Triaged and In Progress only

        SignInAs(AppRoles.QualityAnalyst);
        var qa = Render<Home>();
        qa.WaitForAssertion(() => Assert.Equal("0", Kpi(qa, "kpi-mine")));
        Assert.Contains("Your role has no status changes to make", qa.Find("[data-testid=my-tasks]").TextContent);
    }

    [Fact]
    public async Task Quick_actions_follow_permissions()
    {
        await SeedMixAsync();
        SignInAs(AppRoles.CaseWorker);
        var worker = Render<Home>();
        worker.WaitForAssertion(() => Assert.Contains("New case", worker.Find("[data-testid=quick-actions]").TextContent));
        Assert.DoesNotContain("Test results", worker.Find("[data-testid=quick-actions]").TextContent);

        SignInAs(AppRoles.QualityAnalyst);
        var qa = Render<Home>();
        qa.WaitForAssertion(() => Assert.Contains("Test results", qa.Find("[data-testid=quick-actions]").TextContent));
        Assert.DoesNotContain("New case", qa.Find("[data-testid=quick-actions]").TextContent);
    }

    [Fact]
    public async Task Charts_are_accessible_figures_with_summaries_and_data_tables()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var status = cut.WaitForElement("[data-testid=chart-status]");
        Assert.Equal("Cases by status", status.QuerySelector("figcaption")!.TextContent);
        var svg = status.QuerySelector("svg")!;
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Contains("New: 2; Triaged: 1; In Progress: 1; Resolved: 1; Closed: 1.", status.QuerySelector("desc")!.TextContent);
        Assert.Equal(5, status.QuerySelectorAll("details table tbody tr").Length);
        Assert.Contains("Not available", cut.Find("[data-testid=chart-priority]").TextContent);
        Assert.Contains("Not available", cut.Find("[data-testid=chart-category]").TextContent);
    }

    [Fact]
    public void An_empty_system_shows_an_empty_state_with_a_create_action_for_creators()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("No cases yet", cut.Find("[data-testid=dashboard-empty]").TextContent));
        Assert.Contains(cut.FindAll("[data-testid=dashboard-empty] a"), a => a.GetAttribute("href") == "cases/new");
    }

    [Fact]
    public async Task A_failed_load_shows_an_alert_and_retry_reloads()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedMixAsync();
        CaseService.ListError = new Contracts.Common.Error(ErrorKind.Forbidden, "Forbidden", "You do not have permission to perform this action.");
        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("The dashboard could not be loaded", cut.Find("[data-testid=dashboard-error]").TextContent));
        Assert.Equal("alert", cut.Find("[data-testid=dashboard-error]").GetAttribute("role"));
        Assert.Empty(cut.FindAll("[data-testid=kpis]"));

        CaseService.ListError = null;
        cut.Find("[data-testid=dashboard-retry]").Click();

        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));
    }

    [Fact]
    public void Chart_without_data_says_so_instead_of_drawing_an_empty_axis()
    {
        var cut = Render<Chart>(p => p.Add(c => c.Id, "c").Add(c => c.Title, "Empty").Add(c => c.Categories, ["A", "B"])
            .Add(c => c.Series, [new ChartSeries("S", [0, 0])]).Add(c => c.EmptyText, "Nothing yet."));
        Assert.Equal("Nothing yet.", cut.Find("[data-testid=chart-empty]").TextContent.Trim());
        Assert.Empty(cut.FindAll("svg[role=img]"));
    }

    [Fact]
    public void Multi_series_chart_has_a_legend_and_distinct_marker_shapes()
    {
        var cut = Render<Chart>(p => p.Add(c => c.Id, "c").Add(c => c.Title, "Trend").Add(c => c.Kind, ChartKind.Line)
            .Add(c => c.Categories, ["Mon", "Tue"]).Add(c => c.Series, [new ChartSeries("Passed", [3, 4]), new ChartSeries("Failed", [1, 0], "--series-2")]));
        Assert.Equal(["Passed", "Failed"], cut.FindAll(".chart-legend li").Select(li => li.TextContent));
        Assert.Equal(2, cut.FindAll("svg circle").Count);
        Assert.Equal(2, cut.FindAll("svg rect").Count);
        Assert.Contains("Mon: Passed 3, Failed 1; Tue: Passed 4, Failed 0.", cut.Find("desc").TextContent);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("cases", "cases")]
    [InlineData("cases?status=New", "cases")]
    [InlineData("cases/12", "cases")]
    [InlineData("cases/new", "cases/new")]
    [InlineData("test-results", "test-results")]
    [InlineData("test-results/runs/5", "test-results/runs")]
    [InlineData("test-results/executions/9", "test-results")]
    [InlineData("unknown", null)]
    public void Exactly_one_navigation_item_is_current_for_a_path(string path, string? expected)
    {
        Assert.Equal(expected, AppNavigation.CurrentHref(path, AppNavigation.Groups.SelectMany(g => g.Items)));
    }
}
