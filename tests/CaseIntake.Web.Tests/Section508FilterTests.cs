using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.Web.Components.Pages.TestResults;

namespace CaseIntake.Web.Tests;

/// <summary>
/// QA test results Section 508 enhancement, page side: the "508 Accessibility" filter (default All Tests, applied as
/// it changes, combined with the other filters, kept in the URL, reset by Reset filters), the badge, the summary over the
/// filtered set with its pass-rate rule, and the filtered export link.
/// </summary>
[Category("Story:QA-508")]
public class Section508FilterTests : WebTestContext
{
    public Section508FilterTests()
    {
        SignInAs(AppRoles.QualityAnalyst);
        TestResults.Runs.Add(FakeTestResultsService.Run(4, passed: 2, failed: 2));
        TestResults.Executions.AddRange(
        [
            Row(1, "Axe sweep", TestOutcome.Passed, AccessibilityClassification.Section508, "E205.4", "2.0", "Multiple", "AA"),
            Row(2, "Keyboard only sign in", TestOutcome.NotExecuted, AccessibilityClassification.Section508, "E205.4", "2.0", "2.1.1", "A"),
            Row(3, "Dialog focus returns", TestOutcome.Failed, AccessibilityClassification.Section508, "E205.4", "2.0", "2.4.3", "A"),
            Row(4, "Reflow at 320 px", TestOutcome.Failed, AccessibilityClassification.Wcag22Project, null, "2.1", "1.4.10", "AA"),
            Row(5, "Times are eastern", TestOutcome.Passed, null, null, null, null, null),
        ]);
    }

    private static TestExecutionRowDto Row(long id, string title, TestOutcome outcome, AccessibilityClassification? a11y, string? provision,
        string? version, string? criterion, string? level) =>
        new(id, 4, id, title, $"Ns.{title}", "E2E", TestType.EndToEnd, outcome, TimeSpan.FromSeconds(1), null, null, null, 0,
            a11y, provision, version, criterion, level, TestMethod.Automated);

    private IRenderedComponent<RunDetail> RenderRun() => Render<RunDetail>(p => p.Add(r => r.RunId, 4));

    [Test]
    public void The_filter_defaults_to_all_tests_and_offers_the_three_choices()
    {
        Navigation.NavigateTo("test-results/runs/4");
        var cut = RenderRun();

        var select = cut.WaitForElement("[data-testid=filter-508]");
        Assert.Equal("508 Accessibility", cut.Find("label[for=execution-508]").TextContent);
        Assert.Equal(["All Tests", "Section 508 Tests Only", "Exclude Section 508 Tests"], select.QuerySelectorAll("option").Select(o => o.TextContent));
        Assert.Equal(nameof(Section508Filter.All), select.GetAttribute("value"));
        Assert.Equal(5, cut.FindAll("[data-testid=execution-row]").Count);
    }

    [Test]
    public void Choosing_Section_508_only_applies_at_once_resets_paging_and_lives_in_the_url()
    {
        Navigation.NavigateTo("test-results/runs/4?page=2");
        var cut = RenderRun();

        cut.WaitForElement("[data-testid=filter-508]").Change(nameof(Section508Filter.Section508Only));

        Assert.Contains("a11y=508", Navigation.Uri, StringComparison.Ordinal);
        Assert.DoesNotContain("page=", Navigation.Uri, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=execution-row]").Count));
        Assert.All(cut.FindAll("[data-testid=execution-row]"), r => Assert.Equal(nameof(AccessibilityClassification.Section508), r.GetAttribute("data-a11y")));
        Assert.Equal(Section508Filter.Section508Only, TestResults.LastExecutionQuery!.Section508);
        Assert.Contains("3 results match the filters.", cut.Find("[data-testid=executions-announcement]").TextContent);
    }

    [Test]
    public void Returning_from_a_result_restores_the_filter_and_it_combines_with_the_result_filter()
    {
        Navigation.NavigateTo("test-results/runs/4?a11y=not508&outcome=Failed");

        var cut = RenderRun();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=execution-row]")));
        Assert.Contains("Reflow at 320 px", cut.Find("[data-testid=execution-row]").TextContent);
        Assert.Equal(nameof(Section508Filter.ExcludeSection508), cut.Find("[data-testid=filter-508]").GetAttribute("value"));
        Assert.Equal([TestOutcome.Failed], TestResults.LastExecutionQuery!.Outcomes);
    }

    [Test]
    public void Matching_tests_carry_a_508_badge_and_their_standards_mapping()
    {
        var cut = RenderRun();

        var rows = cut.WaitForElement("[data-testid=executions-table]").QuerySelectorAll("[data-testid=execution-row]");
        var sweep = rows.Single(r => r.TextContent.Contains("Axe sweep", StringComparison.Ordinal));
        Assert.Contains("508 Accessibility", sweep.QuerySelector("[data-testid=badge-508]")!.TextContent);
        Assert.Contains("508 E205.4", sweep.TextContent);
        Assert.Contains("WCAG 2.0 · Multiple · AA", sweep.TextContent);
        var reflow = rows.Single(r => r.TextContent.Contains("Reflow", StringComparison.Ordinal));
        Assert.Empty(reflow.QuerySelectorAll("[data-testid=badge-508]"));
        Assert.NotNull(reflow.QuerySelector("[data-testid=badge-wcag22]"));
        var plain = rows.Single(r => r.TextContent.Contains("Times are eastern", StringComparison.Ordinal));
        Assert.Empty(plain.QuerySelectorAll("[data-testid^=badge]"));
    }

    [Test]
    public void Summary_counts_the_filtered_set_and_says_the_pass_rate_is_not_a_conformance_score()
    {
        Navigation.NavigateTo("test-results/runs/4?a11y=508");

        var cut = RenderRun();

        cut.WaitForAssertion(() => Assert.Equal("3", cut.Find("[data-testid=execution-summary-total]").TextContent));
        Assert.Equal("1", cut.Find("[data-testid=execution-summary-passed]").TextContent);
        Assert.Equal("1", cut.Find("[data-testid=execution-summary-failed]").TextContent);
        Assert.Equal("1", cut.Find("[data-testid=execution-summary-notrun]").TextContent);
        Assert.Equal("0", cut.Find("[data-testid=execution-summary-blocked]").TextContent);
        Assert.Equal("0", cut.Find("[data-testid=execution-summary-na]").TextContent);
        Assert.Equal("50%", cut.Find("[data-testid=execution-summary-rate]").TextContent);
        var note = cut.Find("[data-testid=execution-summary-note]").TextContent;
        Assert.Contains("not a conformance score", note);
        Assert.Contains("do not establish Section 508 conformance", note);
        Assert.DoesNotContain("certified", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Pass_rate_is_NA_when_nothing_passed_or_failed()
    {
        Navigation.NavigateTo("test-results/runs/4?outcome=NotExecuted");

        var cut = RenderRun();

        cut.WaitForAssertion(() => Assert.Equal("N/A", cut.Find("[data-testid=execution-summary-rate]").TextContent));
        Assert.Equal("Not Run", cut.Find("[data-testid=execution-row] .status-pill").TextContent);
    }

    [Test]
    public void Reset_filters_clears_the_508_filter_with_the_others()
    {
        Navigation.NavigateTo("test-results/runs/4?a11y=508&outcome=Failed");
        var cut = RenderRun();

        cut.WaitForElement("[data-testid=reset-execution-filters]").Click();

        Assert.DoesNotContain("a11y=", Navigation.Uri, StringComparison.Ordinal);
        Assert.DoesNotContain("outcome=", Navigation.Uri, StringComparison.Ordinal);
    }

    [Test]
    public void Export_filtered_results_links_to_the_same_criteria()
    {
        Navigation.NavigateTo("test-results/runs/4?a11y=508&outcome=Failed");
        var cut = RenderRun();

        var export = cut.WaitForElement("[data-testid=export-filtered-results]");

        Assert.Contains("Export Filtered Results", export.TextContent);
        Assert.Equal("test-results/executions/export.csv?run=4&outcome=Failed&a11y=508", export.GetAttribute("href"));
    }

    [Test]
    public void Test_case_list_has_the_same_filter_summary_and_export()
    {
        TestResults.Cases.AddRange(
        [
            new TestCaseDto(1, "Ns.A", "Axe sweep", "E2E", TestType.EndToEnd, null, null, null, null, null, null, null, TestOutcome.Passed, 4, DateTimeOffset.UnixEpoch,
                AccessibilityClassification.Section508, "E205.4", "2.0", "Multiple", "AA", TestMethod.Automated),
            new TestCaseDto(2, "Manual.KB-1", "Keyboard only sign in", "Manual accessibility", TestType.Manual, null, null, null, null, null, null, null, null, null, DateTimeOffset.UnixEpoch,
                AccessibilityClassification.Section508, "E205.4", "2.0", "2.1.1", "A", TestMethod.ManualKeyboard),
            new TestCaseDto(3, "Ns.T", "Times are eastern", "E2E", TestType.EndToEnd, null, null, null, null, null, null, null, TestOutcome.Passed, 4, DateTimeOffset.UnixEpoch),
        ]);
        Navigation.NavigateTo("test-results/cases?a11y=508");

        var cut = Render<TestCases>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=case-row]").Count));
        Assert.Equal("1", cut.Find("[data-testid=cases-summary-notrun]").TextContent);   // never run = Not Run, not passed
        Assert.Equal("100%", cut.Find("[data-testid=cases-summary-rate]").TextContent);
        Assert.Contains("Not Run", cut.FindAll("[data-testid=case-row]")[1].TextContent);
        Assert.Equal("test-results/cases/export.csv?a11y=508", cut.Find("[data-testid=export-filtered-cases]").GetAttribute("href"));
        Assert.Contains("Manual keyboard", cut.FindAll("[data-testid=case-row]")[1].TextContent);
    }

    [Test]
    public void Query_string_parsing_is_lenient_and_round_trips()
    {
        var query = TestResultsQueryString.Executions(4, key => key switch { "a11y" => "not508", "outcome" => "Bogus", "page" => "3", _ => null });

        Assert.Equal(Section508Filter.ExcludeSection508, query.Section508);
        Assert.Null(query.Outcomes);
        Assert.Equal(3, query.Page);
        Assert.Equal(Section508Filter.All, TestResultsQueryString.ParseSection508("something else"));
        Assert.Equal("not508", TestResultsQueryString.Parameters(query)["a11y"]);
        Assert.Null(TestResultsQueryString.Parameters(query with { Section508 = Section508Filter.All })["a11y"]);
    }
}
