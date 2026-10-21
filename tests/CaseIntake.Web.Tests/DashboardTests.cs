using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Kit.Charts;
using CaseIntake.Web.Components.Layout;
using CaseIntake.Web.Components.Pages;

namespace CaseIntake.Web.Tests;

/// <summary>Dashboard: KPIs and charts from the authorized case service only, per role; empty, error and retry states.</summary>
[Category("Story:Dashboard")]
[Category("Req:R-4.2-11")]
[Category("Req:R-4.2-12")]
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

    [Test]
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

    [Test]
    public async Task Overdue_follows_the_due_date_not_the_last_update()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        Time.Advance(TimeSpan.FromDays(6));   // 2026-10-25: past the urgent (+2) and high (+5) targets, not the medium (+10) ones

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("2", Kpi(cut, "kpi-overdue")));
        Assert.Equal("cases?overdue=true&sort=DueOn&dir=asc", cut.Find("[data-testid=kpi-overdue]").GetAttribute("href"));
    }

    [Test]
    public async Task Attention_and_recent_case_lists_are_gone_and_my_pending_tasks_is_a_kpi()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));
        Assert.Empty(cut.FindAll("[data-testid=attention]"));
        Assert.Empty(cut.FindAll("[data-testid=recent-cases]"));
        Assert.Empty(cut.FindAll("[data-testid=my-tasks]"));
        var tasks = cut.Find("[data-testid=kpi-tasks]");
        Assert.Equal("my-work", tasks.GetAttribute("href"));
        Assert.Equal("5", Kpi(cut, "kpi-tasks"));   // a supervisor can move every status except Closed
        Assert.Equal(7, cut.FindAll("[data-testid=kpis] > li").Count);
    }

    [Test]
    public async Task Kpi_cards_are_single_links_with_a_details_hint_and_no_nested_controls()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));
        foreach (var card in cut.FindAll("[data-testid=kpis] a.icard"))
        {
            Assert.Empty(card.QuerySelectorAll("a, button, input, select"));
            var hint = card.QuerySelector(".icard__more")!;
            Assert.Contains("View details", hint.TextContent);
            Assert.Equal("true", hint.GetAttribute("aria-hidden"));
        }
        Assert.Contains("icard--red", cut.Find("[data-testid=kpi-overdue]").ClassList);
        Assert.Contains("icard--green", cut.Find("[data-testid=kpi-resolved]").ClassList);
    }

    [Test]
    public async Task Filters_live_in_the_url_narrow_every_figure_and_are_kept_by_the_kpi_links()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        Navigation.NavigateTo("?priority=High&from=2026-10-01&to=2026-10-31");

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("1", Kpi(cut, "kpi-total")));   // only the High (Triaged) case
        Assert.Equal("1", Kpi(cut, "kpi-open"));
        Assert.Equal("10-01-2026", cut.Find("#dash-from").GetAttribute("value"));
        Assert.Equal("High", cut.Find("#dash-priority").GetAttribute("value"));
        Assert.Equal("cases?status=New&status=Triaged&status=InProgress&priority=High&createdFrom=2026-10-01&createdTo=2026-10-31",
            cut.Find("[data-testid=kpi-open]").GetAttribute("href"));
        Assert.Contains("priority=High", cut.Find("[data-testid=quick-export]").GetAttribute("href"));
        Assert.Contains("Export filtered CSV", cut.Find("[data-testid=quick-export]").TextContent);
        Assert.Contains("created", cut.Find("#dash-basis").TextContent);
        Assert.Contains("Dashboard filtered: 1 case.", cut.Find("[data-testid=dashboard-announcement]").TextContent);
    }

    [Test]
    public async Task Changing_a_filter_applies_it_and_reset_clears_every_filter()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));
        Assert.True(cut.Find("[data-testid=dash-reset]").HasAttribute("disabled"));

        cut.Find("#dash-status").Change("New");

        Assert.Contains("status=New", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("2", Kpi(cut, "kpi-total")));
        cut.Find("[data-testid=dash-reset]").Click();
        Assert.DoesNotContain("status=", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("6", Kpi(cut, "kpi-total")));
    }

    [Test]
    public async Task Status_is_a_donut_and_open_priority_a_pie_with_counts_and_shares_as_text()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var status = cut.WaitForElement("[data-testid=chart-status]");
        Assert.Equal("6", status.QuerySelector(".chart__center-value")!.TextContent);
        var legend = status.QuerySelectorAll("[data-testid=chart-status-legend] li").Select(li => string.Concat(li.TextContent.Where(ch => !char.IsWhiteSpace(ch)))).ToList();
        Assert.Contains("New233.3%", legend);
        Assert.Equal("var(--status-new-c)", status.QuerySelector(".chart-legend__swatch")!.GetAttribute("style")!.Replace("background: ", ""));
        // Slices are mouse targets only (no links inside the role="img" chart: axe nested-interactive); the legend links
        // are the keyboard and screen-reader path (one tab stop per value).
        Assert.Empty(status.QuerySelectorAll("svg a, svg [tabindex], svg .chart__point[aria-label]"));
        Assert.Equal(5, status.QuerySelectorAll("svg .chart__point--link[data-href]").Length);
        Assert.Equal("New: 2 cases, 33.3%. Open the list.", status.QuerySelector("[data-testid=chart-status-legend] a")!.GetAttribute("aria-label"));

        var priority = cut.Find("[data-testid=chart-priority]");
        Assert.Empty(priority.QuerySelectorAll(".chart__center-value"));   // a pie, not a donut
        Assert.Contains("Urgent125%", priority.QuerySelector("[data-testid=chart-priority-legend]")!.TextContent.Replace(" ", "").Replace("\n", "").Replace("\r", ""));
        Assert.Contains("Total", priority.QuerySelector("details tfoot")!.TextContent);
    }

    [Test]
    public async Task Resolution_trend_is_a_line_that_says_it_approximates_completion_by_last_update()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var chart = cut.WaitForElement("[data-testid=chart-resolution]");
        Assert.NotNull(chart.QuerySelector("polyline.chart__line"));
        Assert.Equal(12, chart.QuerySelectorAll("circle.chart__marker").Length);
        Assert.Contains("Approximation", chart.QuerySelector(".chart__desc")!.TextContent);
        Assert.Contains("last update", chart.QuerySelector(".chart__desc")!.TextContent);
        // Chronological months, oldest first.
        var months = chart.QuerySelectorAll("details tbody th").Select(th => th.TextContent.Trim()).ToList();
        Assert.Equal("Nov 2025", months[0]);
        Assert.Equal("Oct 2026", months[^1]);
    }

    [Test]
    public async Task Charts_offer_a_maximize_button_and_the_card_background_opens_the_same_view()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var chart = cut.WaitForElement("[data-testid=chart-status]");
        Assert.Contains("M15 3h6v6", cut.Find("[data-testid=chart-status-expand]").InnerHtml, StringComparison.Ordinal);   // the maximize icon, not a zoom lens
        var backdrop = chart.QuerySelector(".card__backdrop-action")!;
        Assert.Equal("true", backdrop.GetAttribute("aria-hidden"));

        cut.Find("[data-testid=chart-status] .card__backdrop-action").Click();

        cut.WaitForAssertion(() => Assert.Equal("Cases by status", cut.Find("#chart-status-zoom-title").TextContent));
    }

    [Test]
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
        Assert.Equal("0", Kpi(qa, "kpi-tasks"));   // a role with no status changes has no pending tasks
    }

    [Test]
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
        cut.WaitForAssertion(() => Assert.Equal("Dashboard refreshed at 08:00:42 ET", Announcer.Message), TimeSpan.FromSeconds(5));
        Assert.Contains("Refresh", cut.Find("[data-testid=dashboard-refresh]").TextContent);
        Assert.Contains("Updated 08:00:42 ET", cut.Find("[data-testid=dashboard-updated]").TextContent);
        Assert.Equal("Monday, 10-19-2026", cut.Find("[data-testid=dashboard-date]").TextContent);
        Assert.Equal("Good morning, Supervisor", cut.Find("h1").TextContent);
    }

    [Test]
    public async Task Quick_actions_follow_permissions()
    {
        await SeedMixAsync();
        SignInAs(AppRoles.CaseWorker);
        var worker = Render<Home>();
        worker.WaitForAssertion(() => Assert.Contains("New case", worker.Find("[data-testid=quick-actions]").TextContent));
        Assert.DoesNotContain("Test results", worker.Find("[data-testid=quick-actions]").TextContent);
        Assert.Empty(worker.FindAll("[data-testid=quick-add-user]"));
        Assert.Contains("Export CSV", worker.Find("[data-testid=quick-export]").TextContent);
        Assert.All(worker.FindAll("[data-testid=quick-actions] a"), a => Assert.Contains("btn", a.ClassList));

        SignInAs(AppRoles.Supervisor);
        var supervisor = Render<Home>();
        supervisor.WaitForAssertion(() => Assert.Equal("admin/users#add-user", supervisor.Find("[data-testid=quick-add-user]").GetAttribute("href")));

        SignInAs(AppRoles.QualityAnalyst);
        var qa = Render<Home>();
        qa.WaitForAssertion(() => Assert.Contains("Test results", qa.Find("[data-testid=quick-actions]").TextContent));
        Assert.DoesNotContain("New case", qa.Find("[data-testid=quick-actions]").TextContent);
    }

    [Test]
    public async Task Charts_are_accessible_figures_with_summaries_and_data_tables()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var status = cut.WaitForElement("[data-testid=chart-status]");
        Assert.Equal("Cases by status", status.QuerySelector("figcaption")!.TextContent);
        var svg = status.QuerySelector("svg")!;
        Assert.Equal("img", svg.GetAttribute("role"));   // a donut: the legend holds the drill-through links
        Assert.Contains("New: 2; Triaged: 1; In Progress: 1; Resolved: 1; Closed: 1.", status.QuerySelector("desc")!.TextContent);
        Assert.Equal(5, status.QuerySelectorAll("details table tbody tr").Length);

        // Priority and category are real now (issue #22): open cases only.
        var priority = cut.Find("[data-testid=chart-priority]");
        Assert.Contains("Urgent: 1; High: 1; Medium: 2; Low: 0.", priority.QuerySelector("desc")!.TextContent);
        var category = cut.Find("[data-testid=chart-category]");
        Assert.Contains("General: 2; Claims: 1; Billing: 1.", category.QuerySelector("desc")!.TextContent);   // most first, then by category
        Assert.DoesNotContain("Not available", cut.Markup);
    }

    [Test]
    public async Task Chart_columns_and_bars_drill_through_to_the_matching_cases_with_named_links()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var status = cut.WaitForElement("[data-testid=chart-status]");
        var slices = status.QuerySelectorAll("svg .chart__point--link");
        Assert.Equal(5, slices.Length);
        Assert.Equal("cases?status=Triaged", slices[1].GetAttribute("data-href"));
        Assert.Equal("Triaged: 1 case", status.QuerySelector($"#{slices[1].GetAttribute("data-tip")}")!.TextContent);
        var legendLinks = status.QuerySelectorAll("[data-testid=chart-status-legend] a");
        Assert.Equal("cases?status=Triaged", legendLinks[1].GetAttribute("href"));
        Assert.Equal("Triaged: 1 case, 16.7%. Open the list.", legendLinks[1].GetAttribute("aria-label"));

        var volume = cut.Find("[data-testid=chart-volume]");
        var october = volume.QuerySelectorAll("svg a.chart__point").Last();
        Assert.Equal("cases?createdFrom=2026-10-01&createdTo=2026-10-31&sort=CreatedAt&dir=desc", october.GetAttribute("href"));
        Assert.Contains("Oct 2026: 6 cases", volume.QuerySelector($"#{october.GetAttribute("data-tip")}")!.TextContent);
        Assert.Equal("cases?createdFrom=2026-10-01&createdTo=2026-10-31&sort=CreatedAt&dir=desc",
            volume.QuerySelector("details table tbody tr:last-child a")!.GetAttribute("href"));

        var priority = cut.Find("[data-testid=chart-priority]");
        Assert.Equal("cases?status=New&status=Triaged&status=InProgress&priority=Urgent", priority.QuerySelector("svg .chart__point--link")!.GetAttribute("data-href"));
    }

    [Test]
    public async Task Month_axis_labels_are_short_with_the_year_once_and_never_overlap()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedMixAsync();
        var cut = Render<Home>();

        var labels = cut.WaitForElement("[data-testid=chart-volume]").QuerySelectorAll("text.chart__axis-label");
        // Twelve months in a 340-unit chart: every other month is labelled (estimated 4 characters fit one band).
        Assert.Equal(6, labels.Length);
        Assert.Equal("Nov2025", labels[0].TextContent);              // first month: "Nov" with the year on a second line
        Assert.Equal(2, labels[0].QuerySelectorAll("tspan").Length);
        Assert.Equal("Jan2026", labels[1].TextContent);              // every January shows its year
        Assert.Equal("Mar", labels[2].TextContent);
    }

    [Test]
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

    [Test]
    public void An_empty_system_shows_an_empty_state_with_a_create_action_for_creators()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("No cases yet", cut.Find("[data-testid=dashboard-empty]").TextContent));
        Assert.Contains(cut.FindAll("[data-testid=dashboard-empty] a"), a => a.GetAttribute("href") == "cases/new");
    }

    [Test]
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

    [Test]
    public void Chart_without_data_says_so_instead_of_drawing_an_empty_axis()
    {
        var cut = Render<Chart>(p => p.Add(c => c.Id, "c").Add(c => c.Title, "Empty").Add(c => c.Categories, ["A", "B"])
            .Add(c => c.Series, [new ChartSeries("S", [0, 0])]).Add(c => c.EmptyText, "Nothing yet."));
        Assert.Equal("Nothing yet.", cut.Find("[data-testid=chart-empty]").TextContent.Trim());
        Assert.Empty(cut.FindAll("svg[role=img]"));
    }

    [Test]
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
    [TestCase(ChartKind.Column)]
    [TestCase(ChartKind.Line)]
    [TestCase(ChartKind.Bar)]
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

    [Test]
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

    [TestCase("", "")]
    [TestCase("cases", "cases")]
    [TestCase("cases?status=New", "cases")]
    [TestCase("cases/12", "cases")]
    [TestCase("cases/new", "cases/new")]
    [TestCase("test-results", "test-results")]
    [TestCase("test-results/runs/5", "test-results/runs")]
    [TestCase("test-results/executions/9", "test-results")]
    [TestCase("unknown", null)]
    public void Exactly_one_navigation_item_is_current_for_a_path(string path, string? expected)
    {
        Assert.Equal(expected, AppNavigation.CurrentHref(path, AppNavigation.Groups.SelectMany(g => g.Items)));
    }
}
