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
    private const string Supervisor = "supervisor@example.test";
    private const string Worker = "caseworker@example.test";

    /// <summary>Six cases created "now" (2026-10-19) with the default Medium due date (+10 days, 2026-10-29).</summary>
    private async Task SeedMixAsync()
    {
        await SeedCaseAsync("New case one");
        await SeedCaseAsync("New case two");
        await SeedCaseAsync("Triaged case", CaseStatus.Triaged, triage: new CaseTriage(CasePriority.High, CaseCategory.Claims, Supervisor, null));
        await SeedCaseAsync("In progress case", CaseStatus.InProgress, triage: new CaseTriage(CasePriority.Urgent, CaseCategory.Billing, Worker, null));
        await SeedCaseAsync("Resolved case", CaseStatus.Resolved, triage: new CaseTriage(CasePriority.Low, CaseCategory.Claims, Supervisor, null));
        await SeedCaseAsync("Closed case", CaseStatus.Closed);
    }

    private static string Kpi(IRenderedComponent<Home> cut, string id) => cut.Find($"[data-testid={id}] .kpi__value").TextContent;

    [Fact]
    public async Task Kpis_are_computed_from_the_case_service_data()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        Time.Advance(TimeSpan.FromDays(20));   // 2026-11-08: every open case is past its due date (urgent +2, high +5, medium +10)

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));
        Assert.Equal("4", Kpi(cut, "kpi-open"));
        Assert.Equal("1", Kpi(cut, "kpi-pending"));
        Assert.Equal("4", Kpi(cut, "kpi-overdue"));
        Assert.Equal("2", Kpi(cut, "kpi-resolved"));
        Assert.Equal("1", Kpi(cut, "kpi-mine"));   // open cases owned by the supervisor (the Resolved one is not open)
        Assert.All(CaseService.ListQueries, q => Assert.Equal(100, q.PageSize));
    }

    [Fact]
    public async Task Overdue_follows_the_due_date_not_the_last_update()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        Time.Advance(TimeSpan.FromDays(6));   // 2026-10-25: past the urgent (+2) and high (+5) targets, not the medium (+10) ones

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("2", Kpi(cut, "kpi-overdue")));
        Assert.Equal("cases?overdue=true&sort=DueOn&dir=asc", cut.Find("[data-testid=kpi-overdue]").GetAttribute("href"));
        var attention = cut.Find("[data-testid=attention]").TextContent;
        Assert.Contains("In progress case", attention);
        Assert.Contains("Triaged case", attention);
        Assert.DoesNotContain("New case one", attention);
        Assert.Contains("overdue by 4 days", attention);   // urgent: due 2026-10-21
    }

    [Fact]
    public async Task Assigned_to_me_counts_the_open_cases_the_user_owns()
    {
        await SeedMixAsync();
        SignInAs(AppRoles.CaseWorker);
        var worker = Render<Home>();
        worker.WaitForAssertion(() => Assert.Equal("1", Kpi(worker, "kpi-mine")));
        Assert.Equal($"cases?status=New&status=Triaged&status=InProgress&owner={Uri.EscapeDataString(Worker)}",
            worker.Find("[data-testid=kpi-mine]").GetAttribute("href"));

        SignInAs(AppRoles.QualityAnalyst);
        var qa = Render<Home>();
        qa.WaitForAssertion(() => Assert.Equal("0", Kpi(qa, "kpi-mine")));
        Assert.Contains("Your role has no status changes to make", qa.Find("[data-testid=my-tasks]").TextContent);
    }

    [Fact]
    public async Task Recent_cases_lists_only_new_cases_created_in_the_last_seven_days()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("Old new case");
        await SeedCaseAsync("Old triaged case", CaseStatus.Triaged);
        Time.Advance(TimeSpan.FromDays(10));
        await SeedCaseAsync("Fresh new case");
        await SeedCaseAsync("Fresh triaged case", CaseStatus.Triaged);

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("Fresh new case", cut.Find("[data-testid=recent-cases]").TextContent));
        var recent = cut.Find("[data-testid=recent-cases]").TextContent;
        Assert.DoesNotContain("Fresh triaged case", recent);   // not New any more
        Assert.DoesNotContain("Old new case", recent);         // created more than 7 days ago
        Assert.Contains("New cases created in the last 7 days", recent);
    }

    [Fact]
    public async Task Refresh_shows_progress_then_announces_the_new_time()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));

        await SeedCaseAsync("Added meanwhile");
        Time.Advance(TimeSpan.FromSeconds(42));
        cut.Find("[data-testid=dashboard-refresh]").Click();
        Assert.Contains("Refreshing…", cut.Find("[data-testid=dashboard-refresh]").TextContent);

        cut.WaitForAssertion(() => Assert.Equal("7", Kpi(cut, "kpi-total")), TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => Assert.Equal("Dashboard refreshed at 08:00:42", Announcer.Message), TimeSpan.FromSeconds(5));
        Assert.Contains("Refresh", cut.Find("[data-testid=dashboard-refresh]").TextContent);
        Assert.Contains("Updated 2026-10-19 08:00:42.", cut.Find(".page-header__subtitle").TextContent);
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
        Assert.Equal("group", svg.GetAttribute("role"));   // its bars are links (drill-through)
        Assert.Contains("New: 2; Triaged: 1; In Progress: 1; Resolved: 1; Closed: 1.", status.QuerySelector("desc")!.TextContent);
        Assert.Equal(5, status.QuerySelectorAll("details table tbody tr").Length);

        // Priority and category are real now (issue #22): open cases only.
        var priority = cut.Find("[data-testid=chart-priority]");
        Assert.Contains("Urgent: 1; High: 1; Medium: 2; Low: 0.", priority.QuerySelector("desc")!.TextContent);
        var category = cut.Find("[data-testid=chart-category]");
        Assert.Contains("General: 2; Claims: 1; Billing: 1.", category.QuerySelector("desc")!.TextContent);   // most first, then by category
        Assert.DoesNotContain("Not available", cut.Markup);
    }

    [Fact]
    public async Task Chart_columns_and_bars_drill_through_to_the_matching_cases_with_named_links()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var status = cut.WaitForElement("[data-testid=chart-status]");
        var links = status.QuerySelectorAll("svg a.chart__point");
        Assert.Equal(5, links.Length);
        Assert.Equal("cases?status=Triaged", links[1].GetAttribute("href"));
        Assert.Equal("Triaged: 1 case. Open the list.", links[1].GetAttribute("aria-label"));
        Assert.Equal("Triaged: 1 case", status.QuerySelector($"#{links[1].GetAttribute("data-tip")}")!.TextContent);

        var volume = cut.Find("[data-testid=chart-volume]");
        var october = volume.QuerySelectorAll("svg a.chart__point").Last();
        Assert.Equal("cases?createdFrom=2026-10-01&createdTo=2026-10-31&sort=CreatedAt&dir=desc", october.GetAttribute("href"));
        Assert.Contains("Oct 2026: 6 cases", volume.QuerySelector($"#{october.GetAttribute("data-tip")}")!.TextContent);
        Assert.Equal("cases?createdFrom=2026-10-01&createdTo=2026-10-31&sort=CreatedAt&dir=desc",
            volume.QuerySelector("details table tbody tr:last-child a")!.GetAttribute("href"));

        var priority = cut.Find("[data-testid=chart-priority]");
        Assert.Equal("cases?status=New&status=Triaged&status=InProgress&priority=Urgent", priority.QuerySelector("svg a.chart__point")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Month_axis_labels_are_short_with_the_year_once_and_never_overlap()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var labels = cut.WaitForElement("[data-testid=chart-volume]").QuerySelectorAll("text.chart__axis-label");
        // Twelve months in a 420-unit chart: every other month is labelled (estimated 4 characters fit one band).
        Assert.Equal(6, labels.Length);
        Assert.Equal("Nov2025", labels[0].TextContent);              // first month: "Nov" with the year on a second line
        Assert.Equal(2, labels[0].QuerySelectorAll("tspan").Length);
        Assert.Equal("Jan2026", labels[1].TextContent);              // every January shows its year
        Assert.Equal("Mar", labels[2].TextContent);
    }

    [Fact]
    public async Task Expand_opens_a_larger_copy_of_the_chart_in_a_dialog()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var expand = cut.WaitForElement("[data-testid=chart-volume-expand]");
        Assert.Equal("Expand chart: Case volume trend", expand.QuerySelector(".visually-hidden")!.TextContent);
        Assert.Empty(cut.FindAll("#chart-volume-zoomed-caption"));
        expand.Click();

        cut.WaitForAssertion(() => Assert.Equal("Case volume trend", cut.Find("#chart-volume-zoomed-caption").TextContent));
        Assert.Contains("viewBox=\"0 0 860", cut.Find("[data-testid=chart-volume-dialog]").InnerHtml);
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
        Assert.Equal(2, cut.FindAll("svg circle.chart__marker").Count);
        Assert.Equal(2, cut.FindAll("svg rect.chart__marker").Count);
        // Hover or focus shows the point's values in its tooltip (js/app.js).
        Assert.Equal(["Mon: Passed 3, Failed 1", "Tue: Passed 4, Failed 0"], cut.FindAll(".chart__tips > .chart__tip").Select(t => t.TextContent));
        Assert.Equal("img", cut.Find("svg.chart__svg").GetAttribute("role"));   // no links: one image with a text summary
        Assert.Contains("Mon: Passed 3, Failed 1; Tue: Passed 4, Failed 0.", cut.Find("desc").TextContent);
    }

    // A per-point <title> made the browser show a second, native tooltip, and a tooltip inside its point was painted over
    // by every later point. Tooltips are now one layer after all points, each named by its point's data-tip.
    [Theory]
    [InlineData(ChartKind.Column)]
    [InlineData(ChartKind.Line)]
    [InlineData(ChartKind.Bar)]
    public void Tooltips_are_drawn_after_every_point_with_no_native_title(ChartKind kind)
    {
        var cut = Render<Chart>(p => p.Add(c => c.Id, "c").Add(c => c.Title, "Cases by status").Add(c => c.Kind, kind)
            .Add(c => c.Categories, ["New", "Triaged", "In Progress"]).Add(c => c.Series, [new ChartSeries("Cases", [12, 12, 24])])
            .Add(c => c.Links, ["cases?status=New", "cases?status=Triaged", "cases?status=InProgress"]).Add(c => c.ValueNoun, "cases"));

        var svg = cut.Find("svg.chart__svg");
        Assert.Empty(svg.QuerySelectorAll("title"));
        Assert.Equal("c-caption c-svg-desc", svg.GetAttribute("aria-labelledby"));   // named by the visible caption
        var layer = svg.Children.Last();
        Assert.Equal("chart__tips", layer.GetAttribute("class"));
        Assert.Equal("true", layer.GetAttribute("aria-hidden"));
        Assert.Empty(svg.QuerySelectorAll(".chart__point .chart__tip"));

        var points = svg.QuerySelectorAll("a.chart__point");
        Assert.Equal(["New: 12 cases", "Triaged: 12 cases", "In Progress: 24 cases"],
            points.Select(point => layer.QuerySelector($"#{point.GetAttribute("data-tip")}")!.TextContent));
    }

    [Fact]
    public void Bar_tooltips_stay_inside_their_own_row()
    {
        const double width = 420;
        string[] categories = ["New", "Triaged", "In Progress", "Resolved", "A very long category name for a bar"];
        var cut = Render<Chart>(p => p.Add(c => c.Id, "c").Add(c => c.Title, "Cases by status").Add(c => c.Kind, ChartKind.Bar)
            .Add(c => c.Categories, categories).Add(c => c.Series, [new ChartSeries("Cases", [12, 12, 24, 1, 12])])
            .Add(c => c.ValueNoun, "cases").Add(c => c.Width, width));

        var points = cut.FindAll(".chart__point");
        Assert.Equal(categories.Length, points.Count);
        foreach (var point in points)
        {
            var row = point.QuerySelector(".chart__hit")!;
            var tip = cut.Find($"#{point.GetAttribute("data-tip")} rect");
            double Attr(AngleSharp.Dom.IElement e, string name) => double.Parse(e.GetAttribute(name)!, System.Globalization.CultureInfo.InvariantCulture);
            var (rowTop, rowBottom) = (Attr(row, "y"), Attr(row, "y") + Attr(row, "height"));
            Assert.InRange(Attr(tip, "y"), rowTop, rowBottom);
            Assert.InRange(Attr(tip, "y") + Attr(tip, "height"), rowTop, rowBottom);
            Assert.InRange(Attr(tip, "x"), 0, width);
            Assert.InRange(Attr(tip, "x") + Attr(tip, "width"), 0, width);
        }
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
