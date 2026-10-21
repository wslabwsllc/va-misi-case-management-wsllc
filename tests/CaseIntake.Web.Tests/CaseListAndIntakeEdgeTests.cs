using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Pages.Cases;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Case list filters, chips, refresh, out-of-range pages, sorting on phones and failures; intake failures and removing
/// a selected veteran.
/// </summary>
[Category("Story:E5-S3")]
[Category("Story:E5-S4")]
[Category("Req:R-4.2-1")]
[Category("Req:R-4.2-2")]
[Category("Req:R-4.2-4")]
public class CaseListAndIntakeEdgeTests : WebTestContext
{
    // ---- Case list ----

    [Test]
    public async Task Priority_and_overdue_filters_are_applied_and_an_unchecked_status_is_dropped()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedCaseAsync("Any case");
        Navigation.NavigateTo("cases?status=New");
        var cut = Render<CaseList>();
        cut.WaitForElement("[data-testid=case-table]");

        // Filters apply as soon as they change. "All statuses" drops the status filter; the priority select carries
        // several priorities as one comma-separated option value.
        cut.Find("[data-testid=filter-status]").Change("");
        cut.Find("[data-testid=filter-priority]").Change($"{nameof(CasePriority.High)},{nameof(CasePriority.Urgent)}");
        cut.Find("[data-testid=filter-category]").Change(nameof(CaseCategory.Billing));
        cut.Find("[data-testid=filter-overdue]").Change(true);

        Assert.DoesNotContain("status=", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("priority=High", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("priority=Urgent", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("category=Billing", Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("overdue=true", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.True(CaseService.ListQueries.Last().OverdueOnly));
        Assert.Equal([CasePriority.High, CasePriority.Urgent], CaseService.ListQueries.Last().Priorities!.Order());
        Assert.Equal([CaseCategory.Billing], CaseService.ListQueries.Last().Categories);
    }

    [Test]
    public void Clear_resets_every_filter_and_keeps_the_sort()
    {
        SignInAs(AppRoles.CaseWorker);
        Navigation.NavigateTo("cases?status=New&priority=High&q=abc&overdue=true&owner=someone%40example.test&sort=Title&dir=asc");
        var cut = Render<CaseList>();
        cut.WaitForElement("[data-testid=list-empty]");
        Assert.False(cut.Find("[data-testid=clear-filters]").HasAttribute("disabled"));

        cut.Find("[data-testid=clear-filters]").Click();

        Assert.EndsWith("cases?sort=Title&dir=asc", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid=clear-filters]").HasAttribute("disabled")));
        var query = CaseService.ListQueries.Last();
        Assert.Null(query.Search);
        Assert.False(query.OverdueOnly);
        Assert.Null(query.AssignedTo);
    }

    [Test]
    public void An_owner_from_the_url_who_is_not_in_the_directory_stays_selectable()
    {
        SignInAs(AppRoles.Supervisor);
        Navigation.NavigateTo("cases?owner=former%40example.test");

        var cut = Render<CaseList>();

        cut.WaitForElement("[data-testid=list-empty]");
        Assert.Contains(cut.FindAll("[data-testid=filter-owner] option"), o => o.GetAttribute("value") == "former@example.test" && o.TextContent == "former@example.test");
        Assert.Equal("former@example.test", CaseService.ListQueries.Last().AssignedTo);
    }
    [TestCase("updatedFrom=2026-10-01", "Updated on or after 10-01-2026")]
    [TestCase("updatedTo=2026-10-15", "Updated on or before 10-15-2026")]
    [TestCase("createdTo=2026-10-15", "Created on or before 10-15-2026")]
    [TestCase("createdFrom=2026-10-01", "Created on or after 10-01-2026")]
    public void Open_ended_date_filters_read_as_chips_that_can_be_removed(string filter, string chip)
    {
        SignInAs(AppRoles.CaseWorker);
        Navigation.NavigateTo($"cases?{filter}&priority=Low");
        var cut = Render<CaseList>();

        var chips = cut.WaitForElement("[data-testid=filter-chips]");
        Assert.Contains(chip, chips.TextContent);
        Assert.Equal($"Remove filter: {chip}", chips.QuerySelector("button")!.GetAttribute("aria-label"));

        chips.QuerySelector("button")!.Click();

        Assert.DoesNotContain(filter.Split('=')[0], Navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("priority=Low", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=filter-chips]")));
    }

    [Test]
    public async Task Refresh_reloads_and_announces_the_range()
    {
        SignInAs(AppRoles.CaseWorker);
        await SeedCaseAsync("Refresh me");
        var cut = Render<CaseList>();
        cut.WaitForElement("[data-testid=case-table]");
        var before = CaseService.ListQueries.Count;

        cut.Find("[data-testid=refresh]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Case list refreshed. Showing 1–1 of 1 cases.", Announcer.Message), TimeSpan.FromSeconds(5));
        Assert.Equal(before + 1, CaseService.ListQueries.Count);
        Assert.Contains("Refresh", cut.Find("[data-testid=refresh]").TextContent);
    }

    [Test]
    public async Task A_page_past_the_end_shows_the_last_page()
    {
        SignInAs(AppRoles.CaseWorker);
        for (var i = 0; i < 12; i++)
        {
            await SeedCaseAsync($"Paged case {i:00}");
        }
        Navigation.NavigateTo("cases?page=9&size=10");

        var cut = Render<CaseList>();

        cut.WaitForAssertion(() => Assert.Equal("Page 2 of 2", cut.Find("[data-testid=page-info]").TextContent));
        Assert.EndsWith("cases?page=2&size=10", Navigation.Uri, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll("[data-testid=case-table] tbody tr").Count);
    }

    [Test]
    public async Task Going_back_to_the_first_page_keeps_focus_on_the_pager()
    {
        SignInAs(AppRoles.CaseWorker);
        for (var i = 0; i < 12; i++)
        {
            await SeedCaseAsync($"Paged case {i:00}");
        }
        Navigation.NavigateTo("cases?page=2&size=10");
        var cut = Render<CaseList>();
        cut.WaitForAssertion(() => Assert.Equal("Page 2 of 2", cut.Find("[data-testid=page-info]").TextContent));

        cut.Find("[data-testid=page-previous]").Click();

        Assert.EndsWith("cases?size=10", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal("Page 1 of 2", cut.Find("[data-testid=page-info]").TextContent));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Test]
    public void The_phone_sort_menu_navigates_with_field_and_direction_and_ignores_unknown_values()
    {
        SignInAs(AppRoles.CaseWorker);
        Navigation.NavigateTo("cases");
        var cut = Render<CaseList>();
        Assert.Equal("UpdatedAt:desc", cut.Find("#case-sort").GetAttribute("value"));

        cut.Find("#case-sort").Change("DueOn:asc");
        Assert.EndsWith("cases?sort=DueOn&dir=asc", Navigation.Uri, StringComparison.Ordinal);
        cut.Find("#case-sort").Change("Priority:desc");
        Assert.EndsWith("cases?sort=Priority&dir=desc", Navigation.Uri, StringComparison.Ordinal);

        cut.Find("#case-sort").Change("nonsense");
        Assert.EndsWith("cases?sort=Priority&dir=desc", Navigation.Uri, StringComparison.Ordinal);
    }

    [Test]
    public void Unexpected_list_failures_and_loading_are_shown()
    {
        SignInAs(AppRoles.CaseWorker);
        CaseService.ListException = new InvalidOperationException("sql timeout");
        var failed = Render<CaseList>();
        failed.WaitForAssertion(() => Assert.Contains("The case list could not be loaded. Try again.", failed.Find("[data-testid=list-error]").TextContent));
        Assert.DoesNotContain("sql timeout", failed.Markup);
        Assert.Empty(failed.FindAll("[data-testid=case-table]"));

        CaseService.ListException = null;
        CaseService.ListGate = new TaskCompletionSource();
        var loading = Render<CaseList>();
        Assert.Contains("Loading cases…", loading.Find("[data-testid=loading]").TextContent);
        Assert.Equal("true", loading.Find("[data-testid=refresh]").GetAttribute("aria-busy"));
        CaseService.ListGate.SetResult();
        loading.WaitForElement("[data-testid=list-empty]");
    }

    [Test]
    public void Long_searches_are_cut_to_the_maximum_length()
    {
        var state = CaseListState.Parse(1, 25, null, new string('x', CaseFieldLimits.SearchMaxLength + 50), null, null);
        Assert.Equal(CaseFieldLimits.SearchMaxLength, state.Search!.Length);
    }

    // ---- Intake ----

    [Test]
    public void An_unexpected_intake_failure_keeps_the_entries_and_shows_a_generic_alert()
    {
        SignInAs(AppRoles.CaseWorker);
        CaseService.CreateException = new InvalidOperationException("db");
        var cut = Render<NewCase>();
        cut.Find("#intake-title").Input("Keep this title");
        AttachVeteran(cut);

        cut.Find("[data-testid=intake-form]").Submit();
        cut.Find("[data-testid=confirm-intake-submit]").Click();

        Assert.Contains("Something went wrong and the case was not saved. Your entries are kept; try again.", cut.Find("[data-testid=intake-error]").TextContent);
        Assert.Equal("Keep this title", cut.Find("#intake-title").GetAttribute("value"));
        Assert.Equal("false", cut.Find("[data-testid=create-case]").GetAttribute("aria-busy"));
    }

    [Test]
    public void Veteran_field_errors_from_the_server_are_listed_in_the_summary()
    {
        SignInAs(AppRoles.CaseWorker);
        CaseService.CreateError = new Error(ErrorKind.Validation, "Validation", "Fix these.",
            new Dictionary<string, string[]> { [CaseFieldNames.VeteranAddress] = ["Address is too long."] });
        var cut = Render<NewCase>();
        cut.Find("#intake-title").Input("A valid title");
        AttachVeteran(cut);

        cut.Find("[data-testid=intake-form]").Submit();
        cut.Find("[data-testid=confirm-intake-submit]").Click();

        Assert.Contains("Veteran: Address is too long.", cut.Find("#error-summary").TextContent);
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);   // "Use this veteran" focuses Remove, then the summary
    }

    [Test]
    public void Removing_the_selected_veteran_announces_it_and_returns_to_the_ssn_field()
    {
        SignInAs(AppRoles.CaseWorker);
        Lookup.Returns("900-12-3456", new VeteranLookupResult(VeteranLookupOutcome.Found, "Rowan Calloway", "Somewhere"));
        var cut = Render<NewCase>();
        cut.Find("#lookup-ssn").Input("900-12-3456");
        cut.Find("[data-testid=lookup-form]").Submit();
        cut.Find("[data-testid=use-veteran]").Click();
        var focusCalls = JSInterop.Invocations.Count(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

        cut.Find("[data-testid=selected-veteran] button").Click();

        Assert.Empty(cut.FindAll("[data-testid=selected-veteran]"));
        Assert.Equal("Veteran removed from the case.", Announcer.Message);
        Assert.True(JSInterop.Invocations.Count(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase)) > focusCalls);
    }
}
