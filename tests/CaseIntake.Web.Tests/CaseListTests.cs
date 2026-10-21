using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Pages.Cases;

namespace CaseIntake.Web.Tests;

/// <summary>E5-S3: server-side paging, filtering and sorting parameters, aria-sort, announcements and URL state.</summary>
[Category("Story:E5-S3")]
[Category("Req:R-4.2-1")]
[Category("Req:R-4.2-2")]
[Category("Req:R-4.2-3")]
[Category("Req:R-4.2-12")]
public class CaseListTests : WebTestContext
{
    private async Task SeedAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await SeedCaseAsync($"Case number {i:00}", i % 3 == 0 ? CaseStatus.InProgress : CaseStatus.New);
        }
    }

    [Test]
    public async Task Loads_the_first_page_into_a_captioned_table_with_the_required_columns()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(30);

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Equal(25, cut.FindAll("[data-testid=case-table] tbody tr").Count));
        // The table is named by the visible "Cases (30 cases)" heading.
        var table = cut.Find("[data-testid=case-table]");
        Assert.StartsWith("Cases", cut.Find($"#{table.GetAttribute("aria-labelledby")}").TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal(["ID", "Title", "Status", "Priority", "Category", "Owner", "Due date", "Updated", "Actions"],
            cut.FindAll("[data-testid=case-table] thead th").Select(th => th.TextContent.Trim().Trim('▲', '▼', '⇅').Trim()));
        // Every data column sorts; the row menu column does not.
        Assert.Equal(8, cut.FindAll("[data-testid=case-table] thead th button.sort-button").Count);
        Assert.Equal("Actions for case 1", cut.Find("[data-testid=row-menu] summary").GetAttribute("aria-label"));
        Assert.Equal(["cases/1", "cases/1?edit=true", "cases/1?tab=attachments", "cases/1?tab=history"],
            cut.Find("[data-testid=row-menu]").QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
        var query = CaseService.ListQueries.Last();
        Assert.Equal(new CaseListQuery(), query with { Statuses = null });
        Assert.Equal("30 cases found. Showing 1–25 of 30 cases. Page 1 of 2.", cut.Find("[data-testid=results-announcement]").TextContent);
        Assert.Equal("Showing 1–25 of 30 cases", cut.Find("[data-testid=page-range]").TextContent);
        Assert.Equal("polite", cut.Find("[data-testid=results-announcement]").GetAttribute("aria-live"));
        Assert.Equal("descending", cut.Find("th[aria-sort]").GetAttribute("aria-sort"));
    }

    [Test]
    public async Task Query_string_restores_filter_sort_and_page_and_passes_them_to_the_service()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(40);
        Navigation.NavigateTo("cases?page=2&size=10&status=New&q=number&sort=Title&dir=asc");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=case-table] tbody tr")));
        var query = CaseService.ListQueries.Last();
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
        Assert.Equal([CaseStatus.New], query.Statuses);
        Assert.Equal("number", query.Search);
        Assert.Equal(CaseSortField.Title, query.SortBy);
        Assert.Equal(SortDirection.Ascending, query.Direction);
        Assert.Equal("ascending", cut.Find("th[aria-sort]").GetAttribute("aria-sort"));
        Assert.Contains("Title", cut.Find("th[aria-sort]").TextContent);
        Assert.Equal("New", cut.Find("[data-testid=filter-status]").GetAttribute("value"));
        Assert.Equal("number", cut.Find("#case-search").GetAttribute("value"));
    }

    [Test]
    public async Task Activating_a_sort_header_updates_the_url_and_toggles_direction()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(3);
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();

        cut.Find("[data-testid=sort-Title]").Click();
        Assert.EndsWith("cases?sort=Title&dir=asc", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("ascending", cut.Find("th[aria-sort]").GetAttribute("aria-sort")));

        cut.Find("[data-testid=sort-Title]").Click();
        Assert.EndsWith("cases?sort=Title&dir=desc", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("descending", cut.Find("th[aria-sort]").GetAttribute("aria-sort")));
    }

    [Test]
    public async Task Filters_apply_as_they_change_without_an_apply_button_and_announce_the_count()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(9);
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();
        Assert.Empty(cut.FindAll("[data-testid=apply-filters]"));

        cut.Find("[data-testid=filter-status]").Change("InProgress");

        Assert.Contains("status=InProgress", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("3 cases found. Showing 1–3 of 3 cases. Page 1 of 1.", cut.Find("[data-testid=results-announcement]").TextContent));
        Assert.Equal([CaseStatus.InProgress], CaseService.ListQueries.Last().Statuses);
    }

    [Test]
    public async Task Search_waits_for_a_pause_in_typing_then_applies_from_page_one()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(30);
        Navigation.NavigateTo("cases?page=2");
        var cut = Render<CaseList>();

        cut.Find("#case-search").Input("number 0");

        // Not applied on the keystroke itself (300 ms debounce)...
        Assert.DoesNotContain("q=", Navigation.Uri, StringComparison.Ordinal);
        // ...then applied, back on page 1.
        cut.WaitForAssertion(() => Assert.Contains("q=number%200", Navigation.Uri, StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        Assert.DoesNotContain("page=", Navigation.Uri, StringComparison.Ordinal);
    }

    [Test]
    public async Task Search_by_case_number_finds_that_case()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(12);
        Navigation.NavigateTo("cases?q=%2311");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=case-table] tbody tr")));
        Assert.Contains("Case number 11", cut.Find("[data-testid=case-table] tbody").TextContent);
        Assert.Equal("Search by case ID, title or description", cut.Find("label[for=case-search]").TextContent);
    }

    [Test]
    public async Task Due_date_range_is_typed_as_MM_DD_YYYY_and_filters_on_the_server()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("Due early", triage: new CaseTriage(CasePriority.Medium, CaseCategory.General, null, new DateOnly(2026, 10, 25)));
        await SeedCaseAsync("Due late", triage: new CaseTriage(CasePriority.Medium, CaseCategory.General, null, new DateOnly(2026, 12, 5)));
        await SeedCaseAsync("No due date");
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();

        cut.Find("#filter-due-from").Change("11-01-2026");

        Assert.Contains("dueFrom=2026-11-01", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=case-table] tbody tr")));
        Assert.Equal(new DateOnly(2026, 11, 1), CaseService.ListQueries.Last().DueFrom);
        Assert.Equal("11-01-2026", cut.Find("#filter-due-from").GetAttribute("value"));
        Assert.Contains("Format: MM-DD-YYYY", cut.Find("#filter-due-from-format").TextContent);
        Assert.Contains("filter-due-from-format", cut.Find("#filter-due-from").GetAttribute("aria-describedby"));
    }

    [Test]
    public void A_date_that_is_not_real_is_explained_and_not_applied()
    {
        SignInAs(AppRoles.Supervisor);
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();

        cut.Find("#filter-due-to").Change("02-30-2026");

        Assert.DoesNotContain("dueTo", Navigation.Uri, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#filter-due-to").GetAttribute("aria-invalid"));
        Assert.Contains("MM-DD-YYYY", cut.Find("#filter-due-to-format-error").TextContent);
        Assert.Equal("02-30-2026", cut.Find("#filter-due-to").GetAttribute("value"));
    }

    [Test]
    public async Task A_reversed_due_range_is_explained_and_not_sent()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("Any case");
        Navigation.NavigateTo("cases?dueFrom=2026-11-10");
        var cut = Render<CaseList>();

        cut.Find("#filter-due-to").Change("11-01-2026");

        Assert.DoesNotContain("dueTo", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("on or after", cut.Find("#filter-due-range-error").TextContent);
    }

    [Test]
    public async Task Rows_show_a_description_preview_or_say_none_was_provided()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedCaseAsync("With details", description: "First line\nSecond line");
        await SeedCaseAsync("Without details");
        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=row-description]").Count));
        var previews = cut.FindAll("[data-testid=row-description]").Select(p => p.TextContent).ToList();
        Assert.Contains("First line Second line", previews);
        Assert.Contains("No description provided.", previews);
    }

    [Test]
    public async Task Export_says_whether_it_is_filtered_and_uses_the_same_filters()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(3);
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();
        Assert.Contains("Export all to CSV", cut.Find("[data-testid=export-csv]").TextContent);

        Navigation.NavigateTo("cases?status=New&dueTo=2026-12-31");

        cut.WaitForAssertion(() => Assert.Contains("Export filtered CSV", cut.Find("[data-testid=export-csv]").TextContent));
        var href = cut.Find("[data-testid=export-csv]").GetAttribute("href")!;
        Assert.Contains("status=New", href);
        Assert.Contains("dueTo=2026-12-31", href);
    }

    [Test]
    public async Task Pager_offers_numbered_pages_and_the_documented_sizes()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(30);
        Navigation.NavigateTo("cases?size=10");
        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Equal("Page 1 of 3", cut.Find("[data-testid=page-info]").TextContent));
        Assert.Equal(["10", "25", "50", "100"], cut.FindAll("[data-testid=page-size] option").Select(o => o.TextContent));
        Assert.Equal("page", cut.Find("[data-testid=page-number-1]").GetAttribute("aria-current"));

        cut.Find("[data-testid=page-number-3]").Click();

        Assert.Contains("page=3", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("size=10", Navigation.Uri, StringComparison.Ordinal);
    }

    [Test]
    public async Task Paging_controls_are_labelled_and_move_between_pages()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(30);
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();

        var previous = cut.Find("[data-testid=page-previous]");
        var next = cut.Find("[data-testid=page-next]");
        Assert.Equal("Go to previous page", previous.GetAttribute("aria-label"));
        Assert.Equal("Go to next page", next.GetAttribute("aria-label"));
        Assert.True(previous.HasAttribute("disabled"));
        Assert.Equal("Rows per page", cut.Find("label[for=page-size]").TextContent);

        next.Click();

        Assert.EndsWith("cases?page=2", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("Page 2 of 2", cut.Find("[data-testid=page-info]").TextContent));
        Assert.Contains("Page 2 of 2", cut.Find("[data-testid=results-announcement]").TextContent);
        Assert.Equal(5, cut.FindAll("[data-testid=case-table] tbody tr").Count);
    }

    [Test]
    public async Task Changing_the_page_size_returns_to_page_one()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(30);
        Navigation.NavigateTo("cases?page=2");
        var cut = Render<CaseList>();

        cut.Find("[data-testid=page-size]").Change("50");

        Assert.EndsWith("cases?size=50", Navigation.Uri, StringComparison.Ordinal);
    }

    [Test]
    public void No_matches_show_a_clear_empty_state_with_a_way_out()
    {
        SignInAs(AppRoles.CaseWorker);
        Navigation.NavigateTo("cases?q=nothing-matches");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Contains("No cases match these filters", cut.Find("[data-testid=list-empty]").TextContent));
        Assert.Contains(cut.FindAll("[data-testid=list-empty] button"), b => b.TextContent == "Clear filters");
        Assert.Equal("No cases match the current filters.", cut.Find("[data-testid=results-announcement]").TextContent);
    }

    [Test]
    public async Task Each_title_links_to_its_detail_page_in_both_table_and_phone_cards()
    {
        SignInAs(AppRoles.CaseWorker);
        var created = await SeedCaseAsync("Linked title case");
        var cut = Render<CaseList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"cases/{created.Id}", cut.Find("[data-testid=case-table] tbody a").GetAttribute("href"));
            Assert.Equal($"cases/{created.Id}", cut.Find("[data-testid=case-cards] a").GetAttribute("href"));
        });
    }

    [Test]
    public void A_service_failure_is_shown_in_an_alert()
    {
        SignInAs(AppRoles.CaseWorker);
        CaseService.ListError = new Error(ErrorKind.ExternalUnavailable, "Down", "The case list is unavailable.");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Contains("The case list is unavailable.", cut.Find("[data-testid=list-error]").TextContent));
        Assert.Equal("alert", cut.Find("[data-testid=list-error]").GetAttribute("role"));
    }

    [Test]
    public void New_case_command_requires_the_create_policy()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var cut = Render<CaseList>();
        Assert.Empty(cut.FindAll("[data-testid=new-case]"));
    }

    [Test]
    public async Task Drill_through_parameters_become_service_filters_and_date_chips()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("Urgent claim", triage: new CaseTriage(CasePriority.Urgent, CaseCategory.Claims, "caseworker@example.test", null));
        await SeedCaseAsync("Low billing", triage: new CaseTriage(CasePriority.Low, CaseCategory.Billing, null, null));
        Navigation.NavigateTo("cases?priority=Urgent&category=Claims&owner=caseworker%40example.test&createdFrom=2026-10-01&createdTo=2026-10-31&sort=DueOn&dir=asc");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=case-table] tbody tr")));
        var query = CaseService.ListQueries.Last();
        Assert.Equal([CasePriority.Urgent], query.Priorities);
        Assert.Equal([CaseCategory.Claims], query.Categories);
        Assert.Equal("caseworker@example.test", query.AssignedTo);
        // Whole Eastern days: October 1 starts at 04:00 UTC (EDT); October 31 ends just before November 1 04:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero), query.CreatedFromUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero).AddTicks(-1), query.CreatedToUtc);
        Assert.Equal(CaseSortField.DueOn, query.SortBy);
        Assert.Contains("Urgent claim", cut.Find("[data-testid=case-table]").TextContent);
        Assert.Contains("Urgent", cut.Find("[data-testid=case-table] tbody [data-priority]").TextContent);
        Assert.Equal("Urgent", cut.Find("[data-testid=filter-priority]").GetAttribute("value"));
        Assert.Equal("Claims", cut.Find("[data-testid=filter-category]").GetAttribute("value"));

        var chip = cut.Find("[data-testid=filter-chips]");
        Assert.Contains("Created 10-01-2026 to 10-31-2026", chip.TextContent);
        Assert.Contains("createdFrom=2026-10-01", cut.Find("[data-testid=export-csv]").GetAttribute("href"));
        chip.QuerySelector("button")!.Click();
        Assert.DoesNotContain("createdFrom", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("priority=Urgent", Navigation.Uri, StringComparison.Ordinal);
    }

    [Test]
    public async Task Overdue_filter_keeps_open_cases_past_their_due_date_and_marks_them()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("Due soon", triage: new CaseTriage(CasePriority.Medium, CaseCategory.General, null, new DateOnly(2026, 10, 25)));
        await SeedCaseAsync("Past due", triage: new CaseTriage(CasePriority.High, CaseCategory.General, null, new DateOnly(2026, 10, 15)));
        await SeedCaseAsync("Past due but closed", CaseStatus.Closed, triage: new CaseTriage(CasePriority.High, CaseCategory.General, null, new DateOnly(2026, 10, 10)));
        Navigation.NavigateTo("cases?overdue=true");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=case-table] tbody tr")));
        Assert.True(CaseService.ListQueries.Last().OverdueOnly);
        var row = cut.Find("[data-testid=case-table] tbody tr");
        Assert.Contains("Past due", row.TextContent);
        Assert.Contains("Overdue by 4 days", row.QuerySelector(".cell-overdue")!.TextContent);
        Assert.True(cut.Find("[data-testid=filter-overdue]").HasAttribute("checked"));
    }

    [Test]
    public async Task Owner_filter_offers_me_unassigned_and_the_people_who_work_cases()
    {
        SignInAs(AppRoles.Supervisor);
        await SeedCaseAsync("Any case");
        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=case-table] tbody tr")));
        var options = cut.FindAll("[data-testid=filter-owner] option").Select(o => (o.GetAttribute("value"), o.TextContent)).ToList();
        Assert.Equal(("", "All"), options[0]);
        Assert.Equal(("supervisor@example.test", "Me (supervisor@example.test)"), options[1]);
        Assert.Equal((CaseListQuery.UnassignedFilter, "Unassigned"), options[2]);
        Assert.Equal(("caseworker@example.test", "caseworker@example.test"), options[3]);

        cut.Find("[data-testid=filter-owner]").Change(CaseListQuery.UnassignedFilter);
        Assert.Contains("owner=%28unassigned%29", Navigation.Uri, StringComparison.Ordinal);
    }

    [TestCase(null, null, null, 1, 25, CaseSortField.UpdatedAt, SortDirection.Descending)]
    [TestCase(-3, 7, "Nonsense", 1, 25, CaseSortField.UpdatedAt, SortDirection.Descending)]
    [TestCase(4, 50, "Title", 4, 50, CaseSortField.Title, SortDirection.Ascending)]
    [TestCase(2, 10, "CreatedAt", 2, 10, CaseSortField.CreatedAt, SortDirection.Descending)]
    public void List_state_parsing_is_lenient_and_uses_the_documented_defaults(int? page, int? size, string? sort,
        int expectedPage, int expectedSize, CaseSortField expectedSort, SortDirection expectedDirection)
    {
        var state = CaseListState.Parse(page, size, ["New", "bogus", "new"], "  text  ", sort, null);

        Assert.Equal(expectedPage, state.Page);
        Assert.Equal(expectedSize, state.PageSize);
        Assert.Equal(expectedSort, state.SortBy);
        Assert.Equal(expectedDirection, state.Direction);
        Assert.Equal([CaseStatus.New], state.Statuses);
        Assert.Equal("text", state.Search);
    }
}
