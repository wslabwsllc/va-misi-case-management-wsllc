using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.Persistence;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using CaseIntake.TestResults;
using CaseIntake.TestResults.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>
/// One imported run with automated and manual tests in every classification, shared read-only by
/// <see cref="Section508ResultsTests"/>. Automated: an axe sweep (Section 508), a reflow check (WCAG 2.2 project) and an
/// unclassified test. Manual: one never executed, one claiming Passed without a tester (must stay Not Run), one failed
/// with full evidence (Section 508) and one Not Applicable with full evidence (WCAG 2.2 project).
/// </summary>
public sealed class Section508Fixture : IAsyncLifetime
{
    public const string RunKey = "20261021T120000Z-5080a11y";

    public PostgresDatabaseFixture Database { get; } = new();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "caseintake-508-" + Guid.NewGuid().ToString("N"));
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero));
    public FakeCurrentUser CurrentUser { get; } = new();
    public IDbContextFactory<AppDbContext> Factory { get; private set; } = null!;
    internal TestResultImporter Importer { get; private set; } = null!;
    internal TestResultsService Service { get; private set; } = null!;
    public ImportSummaryDto Imported { get; private set; } = null!;

    public static OperationActor Analyst { get; } =
        FakeCurrentUser.Authenticated("qa-id", "qa@example.test", AppRoles.QualityAnalyst, [Permissions.CasesView, Permissions.TestResultsView]);

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        Factory = new DbFactory(Database.ConnectionString);
        var options = Options.Create(new TestResultsOptions { RootPath = Root });
        Importer = new TestResultImporter(Factory, options, new ConfigurationBuilder().Build(), Time, NullLogger<TestResultImporter>.Instance);
        Service = new TestResultsService(Factory, CurrentUser, Importer, options, Time, NullLogger<TestResultsService>.Instance);
        CurrentUser.Actor = Analyst;
        WriteRun();
        Imported = await Importer.ImportPendingAsync("qa@example.test", "qa-id", CancellationToken.None);
    }

    private void WriteRun()
    {
        var run = Directory.CreateDirectory(Path.Combine(Root, "runs", RunKey)).FullName;
        File.WriteAllText(Path.Combine(run, "CaseIntake.E2E.trx"), """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="1" testName="Ns.Sweep.Every_page_passes_axe" outcome="Passed" duration="00:00:05" startTime="2026-10-21T12:00:01Z" endTime="2026-10-21T12:00:06Z" />
                <UnitTestResult testId="2" testName="Ns.Sweep.Every_page_reflows" outcome="Failed" duration="00:00:02" startTime="2026-10-21T12:00:07Z" endTime="2026-10-21T12:00:09Z">
                  <Output><ErrorInfo><Message>Sideways scrolling on the case list at 320 px.</Message></ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testId="3" testName="Ns.Sweep.Times_are_eastern" outcome="Passed" duration="00:00:01" startTime="2026-10-21T12:00:10Z" endTime="2026-10-21T12:00:11Z" />
              </Results>
              <TestDefinitions>
                <UnitTest id="1"><TestMethod className="Ns.Sweep" name="Every_page_passes_axe" /></UnitTest>
                <UnitTest id="2"><TestMethod className="Ns.Sweep" name="Every_page_reflows" /></UnitTest>
                <UnitTest id="3"><TestMethod className="Ns.Sweep" name="Times_are_eastern" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """);
        Directory.CreateDirectory(Path.Combine(run, "catalog"));
        File.WriteAllText(Path.Combine(run, "catalog", "CaseIntake.E2E.json"), """
            [
              { "method": "Ns.Sweep.Every_page_passes_axe", "categories": ["A11y:Section508", "Section508:E205.4", "Wcag:2.0:Multiple:AA", "Method:Automated", "Workflow:Every signed-in page"] },
              { "method": "Ns.Sweep.Every_page_reflows", "categories": ["A11y:Wcag22", "Wcag:2.1:1.4.10:AA", "Method:Automated"] },
              { "method": "Ns.Sweep.Times_are_eastern", "categories": ["Story:Change-1"] }
            ]
            """);
        Directory.CreateDirectory(Path.Combine(run, "manual"));
        File.WriteAllText(Path.Combine(run, "manual", "manual-test-cases.json"), """
            [
              { "id": "KB-1", "title": "Keyboard only sign in", "workflow": "Sign in", "classification": "Section508", "section508Provision": "E205.4",
                "wcagVersion": "2.0", "wcagCriterion": "2.1.1", "wcagLevel": "A", "method": "ManualKeyboard" },
              { "id": "SR-1", "title": "Screen reader claims a pass without a tester", "classification": "Section508", "method": "ScreenReader",
                "result": { "outcome": "Passed", "executedAtUtc": "2026-10-21T12:30:00Z", "actual": "Sounded fine." } },
              { "id": "KB-2", "title": "Dialog focus returns", "classification": "Section508", "section508Provision": "E205.4", "wcagVersion": "2.0",
                "wcagCriterion": "2.4.3", "wcagLevel": "A", "method": "ManualKeyboard",
                "result": { "outcome": "Failed", "executedAtUtc": "2026-10-21T12:40:00Z", "tester": "QA Tester One", "browser": "Edge 141",
                  "assistiveTechnology": "None (keyboard only)", "actual": "Focus went to the page body after Esc.", "defectReference": "BUG-508-7",
                  "remediationNotes": "Restore focus to the trigger." } },
              { "id": "VIS-1", "title": "Forced colours on the reports page", "classification": "Wcag22", "wcagVersion": "2.2", "wcagCriterion": "1.4.11",
                "wcagLevel": "AA", "method": "VisualOther",
                "result": { "outcome": "NotApplicable", "executedAtUtc": "2026-10-21T12:50:00Z", "tester": "QA Tester One",
                  "actual": "The page has no non-text controls in this release." } }
            ]
            """);
        File.WriteAllText(Path.Combine(run, "run.json"), $$"""
            {
              "schemaVersion": 1, "runKey": "{{RunKey}}", "startedAtUtc": "2026-10-21T12:00:00Z", "finishedAtUtc": "2026-10-21T12:05:00Z",
              "runner": "integration test", "status": "Completed", "environment": { "name": "integration" },
              "suites": [ { "name": "CaseIntake.E2E", "type": "EndToEnd", "trx": "CaseIntake.E2E.trx", "exitCode": 1, "status": "Completed" } ]
            }
            """);
    }

    public async Task DisposeAsync()
    {
        await Database.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class DbFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }
}

/// <summary>QA test results Section 508 enhancement: classification, server-side filter, summary, export and permissions.</summary>
[Category("Story:QA-508")]
public class Section508ResultsTests : ClassFixture<Section508ResultsTests, Section508Fixture>
{
    private static Section508Fixture fixture => Shared;

    private readonly TestResultsService _service = fixture.Service;

    private async Task<long> RunIdAsync()
    {
        await using var db = fixture.Factory.CreateDbContext();
        return await db.TestRuns.Where(r => r.RunKey == Section508Fixture.RunKey).Select(r => r.Id).SingleAsync();
    }

    private static T Ok<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!;
    }

    [Test]
    public async Task Classification_comes_only_from_explicit_metadata_and_older_style_tests_stay_unclassified()
    {
        await using var db = fixture.Factory.CreateDbContext();
        var cases = await db.TestCases.AsNoTracking().ToDictionaryAsync(c => c.Title);

        Assert.Equal(AccessibilityClassification.Section508, cases["Every page passes axe"].Accessibility);
        Assert.Equal("E205.4", cases["Every page passes axe"].Section508Provision);
        Assert.Equal(TestMethod.Automated, cases["Every page passes axe"].Method);
        Assert.Equal(AccessibilityClassification.Wcag22Project, cases["Every page reflows"].Accessibility);
        Assert.Equal(("2.1", "1.4.10", "AA"), (cases["Every page reflows"].WcagVersion, cases["Every page reflows"].WcagCriterion, cases["Every page reflows"].WcagLevel));
        Assert.Null(cases["Times are eastern"].Accessibility);   // not an accessibility test: never auto-classified
        Assert.Null(cases["Times are eastern"].Method);
        Assert.Equal(TestType.Manual, cases["Dialog focus returns"].Type);
        Assert.Equal(TestResultImporter.ManualModule, cases["Dialog focus returns"].Module);
    }

    [Test]
    public async Task Unexecuted_manual_tests_stay_Not_Run_and_a_pass_without_evidence_is_never_recorded()
    {
        var runId = await RunIdAsync();
        var rows = Ok(await _service.ListExecutionsAsync(new TestExecutionQuery(runId, PageSize: 50), CancellationToken.None)).Items
            .ToDictionary(r => r.TestTitle);

        Assert.Equal(TestOutcome.NotExecuted, rows["Keyboard only sign in"].Outcome);
        Assert.Equal(TestOutcome.NotExecuted, rows["Screen reader claims a pass without a tester"].Outcome);
        Assert.Contains(fixture.Imported.Problems, p => p.Contains("SR-1", StringComparison.Ordinal) && p.Contains("Not Run", StringComparison.Ordinal));
        var failed = rows["Dialog focus returns"];
        Assert.Equal(TestOutcome.Failed, failed.Outcome);
        Assert.Equal("QA Tester One", failed.Tester);
        Assert.Equal("BUG-508-7", failed.DefectReference);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 12, 40, 0, TimeSpan.Zero), failed.ExecutedAtUtc);
        Assert.Equal(TestOutcome.NotApplicable, rows["Forced colours on the reports page"].Outcome);

        var detail = Ok(await _service.GetExecutionAsync(failed.Id, CancellationToken.None));
        Assert.Equal("Restore focus to the trigger.", detail.RemediationNotes);
        Assert.Equal("None (keyboard only)", detail.AssistiveTechnology);
        Assert.Equal("Focus went to the page body after Esc.", detail.ActualResult);
    }

    [TestCase(Section508Filter.All, 7)]
    [TestCase(Section508Filter.Section508Only, 4)]
    [TestCase(Section508Filter.ExcludeSection508, 3)]
    public async Task The_508_filter_runs_on_the_server_and_paging_counts_the_whole_filtered_set(Section508Filter filter, int expected)
    {
        var runId = await RunIdAsync();

        var page = Ok(await _service.ListExecutionsAsync(new TestExecutionQuery(runId, PageSize: 2, Section508: filter), CancellationToken.None));

        Assert.Equal(expected, page.TotalCount);
        Assert.Equal(Math.Min(2, expected), page.Items.Count);
        if (filter == Section508Filter.Section508Only)
        {
            Assert.All(page.Items, r => Assert.Equal(AccessibilityClassification.Section508, r.Accessibility));
        }
        if (filter == Section508Filter.ExcludeSection508)
        {
            Assert.All(page.Items, r => Assert.NotEqual(AccessibilityClassification.Section508, r.Accessibility));
        }
        var cases = Ok(await _service.ListTestCasesAsync(new TestCaseQuery(PageSize: 50, Section508: filter), CancellationToken.None));
        Assert.Equal(expected, cases.TotalCount);
    }

    [Test]
    public async Task Summary_counts_the_whole_filtered_set_and_the_pass_rate_is_passed_over_passed_plus_failed()
    {
        var runId = await RunIdAsync();

        var only508 = Ok(await _service.SummarizeExecutionsAsync(new TestExecutionQuery(runId, PageSize: 1, Section508: Section508Filter.Section508Only), CancellationToken.None));

        Assert.Equal(new TestOutcomeCounts(4, Passed: 1, Failed: 1, Skipped: 0, Blocked: 0, NotExecuted: 2, NotApplicable: 0), only508);
        Assert.Equal(0.5, only508.VerdictPassRate);

        var excluded = Ok(await _service.SummarizeExecutionsAsync(new TestExecutionQuery(runId, Section508: Section508Filter.ExcludeSection508), CancellationToken.None));
        Assert.Equal((3, 1, 1, 1), (excluded.Total, excluded.Passed, excluded.Failed, excluded.NotApplicable));

        // Nothing passed or failed: N/A, never 0% or 100%.
        var notRunOnly = Ok(await _service.SummarizeExecutionsAsync(new TestExecutionQuery(runId, [TestOutcome.NotExecuted]), CancellationToken.None));
        Assert.Null(notRunOnly.VerdictPassRate);
    }

    [Test]
    public async Task Filtered_export_uses_the_same_criteria_carries_the_standards_fields_and_is_audited()
    {
        var runId = await RunIdAsync();

        var file = Ok(await _service.ExportExecutionsAsync(new TestExecutionQuery(runId, Section508: Section508Filter.Section508Only), CancellationToken.None));

        var text = System.Text.Encoding.UTF8.GetString(file.Content);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("Section 508 tests only", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("RunId,ExecutionId,TestId,Test,Module,Workflow,Type,Classification,Section508Provision", lines[1], StringComparison.Ordinal);
        Assert.Equal(4, lines.Length - 2);
        Assert.All(lines.Skip(2), l => Assert.Contains("Section 508", l, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Not Run", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("BUG-508-7", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Every page reflows", StringComparison.Ordinal));
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Contains(await db.TestResultAuditEntries.Where(a => a.Action == "ReportExported").Select(a => a.Details).ToListAsync(),
            d => d != null && d.Contains("Section 508 tests only", StringComparison.Ordinal));
    }

    [Test]
    public async Task Without_testresults_view_the_list_summary_and_exports_are_refused()
    {
        var previous = fixture.CurrentUser.Actor;
        fixture.CurrentUser.Actor = FakeCurrentUser.Authenticated("cw-id", "caseworker@example.test", AppRoles.CaseWorker, [Permissions.CasesView]);
        try
        {
            var query = new TestExecutionQuery(Section508: Section508Filter.Section508Only);
            Assert.Equal(ErrorKind.Forbidden, (await _service.ListExecutionsAsync(query, CancellationToken.None)).Error!.Kind);
            Assert.Equal(ErrorKind.Forbidden, (await _service.SummarizeExecutionsAsync(query, CancellationToken.None)).Error!.Kind);
            Assert.Equal(ErrorKind.Forbidden, (await _service.ExportExecutionsAsync(query, CancellationToken.None)).Error!.Kind);
            Assert.Equal(ErrorKind.Forbidden, (await _service.ExportTestCasesAsync(new TestCaseQuery(), CancellationToken.None)).Error!.Kind);
            Assert.Equal(ErrorKind.Forbidden, (await _service.SummarizeTestCasesAsync(new TestCaseQuery(), CancellationToken.None)).Error!.Kind);
        }
        finally
        {
            fixture.CurrentUser.Actor = previous;
        }
    }

    [Test]
    public async Task Importing_again_never_overwrites_the_run_history()
    {
        var before = await RunIdAsync();

        var again = await fixture.Importer.ImportPendingAsync("qa@example.test", "qa-id", CancellationToken.None);

        Assert.Equal(0, again.Imported);
        Assert.Equal(1, again.Skipped);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(1, await db.TestRuns.CountAsync());
        Assert.Equal(before, await RunIdAsync());
        var run = await db.TestRuns.AsNoTracking().SingleAsync();
        Assert.Equal((7, 2, 2, 2, 1), (run.Total, run.Passed, run.Failed, run.NotExecuted, run.NotApplicable));
    }
}
