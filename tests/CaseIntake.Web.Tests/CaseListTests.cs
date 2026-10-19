using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Pages.Cases;

namespace CaseIntake.Web.Tests;

/// <summary>E5-S3: server-side paging, filtering and sorting parameters, aria-sort, announcements and URL state.</summary>
[Trait("Category", "Story:E5-S3")]
[Trait("Category", "Req:R-4.2-1")]
[Trait("Category", "Req:R-4.2-2")]
[Trait("Category", "Req:R-4.2-3")]
[Trait("Category", "Req:R-4.2-12")]
public class CaseListTests : WebTestContext
{
    private async Task SeedAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await SeedCaseAsync($"Case number {i:00}", i % 3 == 0 ? CaseStatus.InProgress : CaseStatus.New);
        }
    }

    [Fact]
    public async Task Loads_the_first_page_into_a_captioned_table_with_the_required_columns()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(30);

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Equal(25, cut.FindAll("[data-testid=case-table] tbody tr").Count));
        Assert.StartsWith("Cases", cut.Find("[data-testid=case-table] caption").TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal(["ID", "Title", "Status", "Created (UTC)", "Updated (UTC)"],
            cut.FindAll("[data-testid=case-table] thead th").Select(th => th.QuerySelector("button")!.TextContent.Trim().Trim('▲', '▼').Trim()));
        var query = CaseService.ListQueries.Last();
        Assert.Equal(new CaseListQuery(), query with { Statuses = null });
        Assert.Equal("Showing 1–25 of 30 cases. Page 1 of 2.", cut.Find("[data-testid=results-announcement]").TextContent);
        Assert.Equal("polite", cut.Find("[data-testid=results-announcement]").GetAttribute("aria-live"));
        Assert.Equal("descending", cut.Find("th[aria-sort]").GetAttribute("aria-sort"));
    }

    [Fact]
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
        Assert.True(cut.Find("[data-testid=status-New]").HasAttribute("checked"));
        Assert.Equal("number", cut.Find("#case-search").GetAttribute("value"));
    }

    [Fact]
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

    [Fact]
    public async Task Applying_status_and_search_filters_navigates_with_them_and_announces_the_range()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(9);
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();

        cut.Find("[data-testid=status-InProgress]").Change(true);
        cut.Find("#case-search").Input("number 0");
        cut.Find("[data-testid=case-filters]").Submit();

        Assert.Contains("status=InProgress", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("q=number%200", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("Showing 1–3 of 3 cases. Page 1 of 1.", cut.Find("[data-testid=results-announcement]").TextContent));
        Assert.Equal([CaseStatus.InProgress], CaseService.ListQueries.Last().Statuses);
    }

    [Fact]
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

    [Fact]
    public async Task Changing_the_page_size_returns_to_page_one()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedAsync(30);
        Navigation.NavigateTo("cases?page=2");
        var cut = Render<CaseList>();

        cut.Find("[data-testid=page-size]").Change("50");

        Assert.EndsWith("cases?size=50", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void No_matches_show_a_clear_empty_state_with_a_way_out()
    {
        SignInAs(AppRoles.CaseWorker);
        Navigation.NavigateTo("cases?q=nothing-matches");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Contains("No cases match these filters", cut.Find("[data-testid=list-empty]").TextContent));
        Assert.Contains(cut.FindAll("[data-testid=list-empty] button"), b => b.TextContent == "Clear filters");
        Assert.Equal("No cases match the current filters.", cut.Find("[data-testid=results-announcement]").TextContent);
    }

    [Fact]
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

    [Fact]
    public void A_service_failure_is_shown_in_an_alert()
    {
        SignInAs(AppRoles.CaseWorker);
        CaseService.ListError = new Error(ErrorKind.ExternalUnavailable, "Down", "The case list is unavailable.");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Contains("The case list is unavailable.", cut.Find("[data-testid=list-error]").TextContent));
        Assert.Equal("alert", cut.Find("[data-testid=list-error]").GetAttribute("role"));
    }

    [Fact]
    public void New_case_command_requires_the_create_policy()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var cut = Render<CaseList>();
        Assert.Empty(cut.FindAll("[data-testid=new-case]"));
    }

    [Theory]
    [InlineData(null, null, null, 1, 25, CaseSortField.UpdatedAt, SortDirection.Descending)]
    [InlineData(-3, 7, "Nonsense", 1, 25, CaseSortField.UpdatedAt, SortDirection.Descending)]
    [InlineData(4, 50, "Title", 4, 50, CaseSortField.Title, SortDirection.Ascending)]
    [InlineData(2, 10, "CreatedAt", 2, 10, CaseSortField.CreatedAt, SortDirection.Descending)]
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
