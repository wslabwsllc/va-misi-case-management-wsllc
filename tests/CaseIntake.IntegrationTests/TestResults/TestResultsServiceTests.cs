using System.Text;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.TestKit;
using CaseIntake.TestResults;
using CaseIntake.TestResults.Import;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>
/// E5: the results service over imported runs: dashboard and comparison, filtered and paged lists with literal (ILike)
/// search, history and flaky tests, audited evidence access and exports, and permission checks on every call.
/// </summary>
[Trait("Category", "Story:E5-S4")]
public sealed class TestResultsServiceTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>, IAsyncLifetime
{
    private const string WebSuite = "CaseIntake.Web.Tests";
    private const string E2ESuite = "CaseIntake.E2E";
    private const string Lists = "Ns.Web.CaseListTests.Lists_cases";
    private const string Filters = "Ns.Web.CaseListTests.Filters_by_status";
    private const string Login = "CaseIntake.E2E.LoginTests.Signs_in_with_the_keyboard";
    private static readonly DateTimeOffset T = TestResultsTestHarness.StartTime;
    private static readonly string LongMessage = "Expected " + new string('x', 400);

    private readonly TestResultsTestHarness _h = new(database);

    public async Task InitializeAsync()
    {
        await database.ResetAsync();
        // C (oldest, nightly, failed to start), A (first full run), B (latest: one newly failed, one fixed, one new, one removed).
        var c = _h.NewRun("20261015-r0", T.AddDays(-1));
        c.Manifest.Status = "FailedToStart";
        c.Manifest.Git = new RunGit { Branch = "nightly", Commit = "fff0000", BuildId = "nightly-7" };
        c.Manifest.Environment = new RunEnvironment { Name = "nightly", Os = "Linux", Runtime = "10.0" };
        c.Complete();

        _h.NewRun("20261016-r1", T)
            .Suite(WebSuite, "Component", Trx.Document(T,
                new TrxTest("Ns.Web.CaseListTests", "Lists_cases"),
                new TrxTest("Ns.Web.CaseListTests", "Filters_by_status", "Failed", Message: LongMessage),
                new TrxTest("Ns.Web.CaseListTests", "Was_blocked", "Aborted"),
                new TrxTest("Ns.Web.CaseListTests", "Removed_later")))
            .Suite(E2ESuite, "EndToEnd", Trx.Document(T, new TrxTest("CaseIntake.E2E.LoginTests", "Signs_in_with_the_keyboard")))
            .Evidence("login", LoginEvidence("1440x900", new EvidenceFile { Kind = "Screenshot", File = "final.png", Caption = "Dashboard after sign in" }),
                ("final.png", [1, 2, 3]))
            .Complete();

        var b = _h.NewRun("20261017-r2", T.AddDays(1))
            .Suite(WebSuite, "Component", Trx.Document(T.AddDays(1),
                new TrxTest("Ns.Web.CaseListTests", "Lists_cases", "Failed", Message: "Expected 3 cases"),
                new TrxTest("Ns.Web.CaseListTests", "Filters_by_status", "Failed", Message: "Still wrong"),
                new TrxTest("Ns.Web.CaseListTests", "Was_blocked"),
                new TrxTest("Ns.Web.CaseListTests", "Brand_new")))
            .Suite(E2ESuite, "EndToEnd", Trx.Document(T.AddDays(1), new TrxTest("CaseIntake.E2E.LoginTests", "Signs_in_with_the_keyboard")))
            .Evidence("login", LoginEvidence("390x844",
                    new EvidenceFile { Kind = "Trace", File = "trace.zip" },
                    new EvidenceFile { Kind = "Screenshot", File = "final.png", Caption = "Dashboard after sign in" }),
                ("final.png", [4, 5, 6, 7]), ("trace.zip", [8]));
        b.Manifest.Git = new RunGit { Branch = "feature/Search_100%", Commit = "abc9999", BuildId = "build-43" };
        b.Complete();

        Assert.Equal(3, (await _h.ImportAsync()).Imported);
        _h.ImporterLogs.Collector.Clear();
    }

    public Task DisposeAsync()
    {
        _h.Dispose();
        return Task.CompletedTask;
    }

    private static EvidenceManifest LoginEvidence(string viewport, params EvidenceFile[] files) => new()
    {
        FullName = Login,
        Title = "Signs in with the keyboard",
        Story = "E5-S8",
        Requirements = ["R-4.2-11", " R-4.2-12 "],
        Browser = "chromium",
        Viewport = viewport,
        Steps =
        [
            new EvidenceStep { Name = "Submit", DurationMs = 30 },
            new EvidenceStep { Name = "Open", DurationMs = 10 },
        ],
        Artifacts = [.. files],
    };

    private TestResultsService Service => _h.Service;

    private static T Ok<T>(Result<T> result) => TestResultsTestHarness.Ok(result);

    private Task<long> RunIdAsync(string key) => _h.QueryAsync(db => db.TestRuns.Where(r => r.RunKey == key).Select(r => r.Id).SingleAsync());

    private Task<long> CaseIdAsync(string fullName) => _h.QueryAsync(db => db.TestCases.Where(c => c.FullName == fullName).Select(c => c.Id).SingleAsync());

    private async Task<string[]> RunKeysAsync(TestRunQuery query) => [.. Ok(await Service.ListRunsAsync(query, default)).Items.Select(r => r.RunKey)];

    [Fact]
    public async Task Dashboard_shows_the_latest_run_its_comparison_and_breakdowns()
    {
        var dashboard = Ok(await Service.GetDashboardAsync(default));

        Assert.Equal("20261017-r2", dashboard.Latest!.RunKey);
        Assert.Equal(new TestOutcomeCounts(5, 3, 2, 0, 0, 0), dashboard.Latest.Counts);
        Assert.Equal(["20261017-r2", "20261016-r1", "20261015-r0"], dashboard.RecentRuns.Select(r => r.RunKey));
        Assert.Equal([new LabelCount("Web.Tests", 2)], dashboard.FailuresByModule);
        Assert.Equal([new LabelCount("chromium 390x844", 1)], dashboard.BrowserCoverage);
        Assert.Equal([new LabelCount("Component (bUnit)", 4), new LabelCount("End-to-end (Playwright)", 1)], dashboard.TestsByType);
        Assert.Equal(6, dashboard.KnownTestCases);
        Assert.Equal("20261016-r1", dashboard.Comparison!.Previous!.RunKey);
    }

    [Fact]
    public async Task Dashboard_is_empty_before_anything_is_imported()
    {
        await database.ResetAsync();

        var dashboard = Ok(await Service.GetDashboardAsync(default));

        Assert.Null(dashboard.Latest);
        Assert.Null(dashboard.Comparison);
        Assert.Empty(dashboard.RecentRuns);
        Assert.Equal(0, dashboard.KnownTestCases);
    }

    [Fact]
    public async Task A_run_is_compared_with_the_run_before_it_by_start_time()
    {
        var comparison = Ok(await Service.CompareWithPreviousAsync(await RunIdAsync("20261017-r2"), default));

        Assert.Equal(("20261017-r2", "20261016-r1"), (comparison.Current.RunKey, comparison.Previous!.RunKey));
        Assert.Equal(["Lists cases"], comparison.NewlyFailed.Select(r => r.TestTitle));
        Assert.Equal(["Was blocked"], comparison.NewlyFixed.Select(r => r.TestTitle));
        Assert.Equal(["Filters by status"], comparison.StillFailing.Select(r => r.TestTitle));
        Assert.Equal(["Brand new"], comparison.NewTests);
        Assert.Equal(["Removed later"], comparison.RemovedTests);

        var first = Ok(await Service.CompareWithPreviousAsync(await RunIdAsync("20261015-r0"), default));
        Assert.Null(first.Previous);
        Assert.Empty(first.NewlyFailed);

        TestResultsTestHarness.Fail(await Service.CompareWithPreviousAsync(-1, default), ErrorKind.NotFound);
    }

    [Fact]
    public async Task The_first_run_with_results_lists_its_failures_and_every_test_as_new()
    {
        var comparison = Ok(await Service.CompareWithPreviousAsync(await RunIdAsync("20261016-r1"), default));

        Assert.Equal("20261015-r0", comparison.Previous!.RunKey);
        Assert.Equal(5, comparison.NewTests.Count);
        Assert.Empty(comparison.RemovedTests);
        Assert.Empty(comparison.NewlyFailed);   // nothing ran before, so nothing is newly failed
    }

    [Fact]
    public async Task Runs_are_listed_newest_first_and_paged()
    {
        var page = Ok(await Service.ListRunsAsync(new TestRunQuery(Page: 2, PageSize: 1), default));

        Assert.Equal((2, 1, 3), (page.Page, page.PageSize, page.TotalCount));
        Assert.Equal("20261016-r1", Assert.Single(page.Items).RunKey);
        Assert.Equal(["20261017-r2", "20261016-r1", "20261015-r0"], await RunKeysAsync(new TestRunQuery()));
    }

    [Fact]
    public async Task Runs_filter_by_status_environment_and_eastern_calendar_days()
    {
        Assert.Equal(["20261015-r0"], await RunKeysAsync(new TestRunQuery(Status: TestRunStatus.FailedToStart)));
        Assert.Equal(["20261015-r0"], await RunKeysAsync(new TestRunQuery(Environment: "nightly")));
        Assert.Equal(["20261016-r1"], await RunKeysAsync(new TestRunQuery(FromDate: new DateOnly(2026, 10, 16), ToDate: new DateOnly(2026, 10, 16))));
        Assert.Equal(["20261017-r2"], await RunKeysAsync(new TestRunQuery(FromDate: new DateOnly(2026, 10, 17))));
        Assert.Equal(["20261016-r1", "20261015-r0"], await RunKeysAsync(new TestRunQuery(ToDate: new DateOnly(2026, 10, 16))));
    }

    [Theory]
    [InlineData("SEARCH", new[] { "20261017-r2" })]          // branch, case-insensitive
    [InlineData("%", new[] { "20261017-r2" })]               // a literal percent, not a wildcard
    [InlineData("_100", new[] { "20261017-r2" })]            // a literal underscore
    [InlineData("Searc__1", new string[0])]                  // "_" must not match any one character
    [InlineData("abc", new[] { "20261017-r2", "20261016-r1" })]   // commit
    [InlineData("NIGHTLY-7", new[] { "20261015-r0" })]       // build id
    [InlineData("20261016", new[] { "20261016-r1" })]        // run key
    [InlineData("  ", new[] { "20261017-r2", "20261016-r1", "20261015-r0" })]
    [InlineData("\\", new string[0])]
    public async Task Run_search_is_a_case_insensitive_literal_substring_match(string search, string[] expected)
    {
        Assert.Equal(expected, await RunKeysAsync(new TestRunQuery(Search: search)));
    }

    [Fact]
    public async Task Very_long_search_text_is_accepted_and_capped()
    {
        Assert.Empty(await RunKeysAsync(new TestRunQuery(Search: "20261016" + new string('z', 400))));
        Assert.Empty(Ok(await Service.ListTestCasesAsync(new TestCaseQuery(Search: new string('a', 300)), default)).Items);
    }

    [Theory]
    [InlineData(0, 25, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 201, "PageSize")]
    public async Task Invalid_paging_is_a_validation_error_on_every_list(int page, int pageSize, string field)
    {
        var errors = new[]
        {
            TestResultsTestHarness.Fail(await Service.ListRunsAsync(new TestRunQuery(page, pageSize), default), ErrorKind.Validation),
            TestResultsTestHarness.Fail(await Service.ListExecutionsAsync(new TestExecutionQuery(Page: page, PageSize: pageSize), default), ErrorKind.Validation),
            TestResultsTestHarness.Fail(await Service.ListTestCasesAsync(new TestCaseQuery(Page: page, PageSize: pageSize), default), ErrorKind.Validation),
            TestResultsTestHarness.Fail(await Service.ListArtifactsAsync(new TestArtifactQuery(Page: page, PageSize: pageSize), default), ErrorKind.Validation),
        };

        Assert.All(errors, e =>
        {
            Assert.Equal("InvalidQuery", e.Code);
            Assert.Equal([field], e.FieldErrors!.Keys);
        });
    }

    [Fact]
    public async Task Run_detail_has_environment_suites_and_run_level_evidence()
    {
        var runId = await RunIdAsync("20261017-r2");

        var detail = Ok(await Service.GetRunAsync(runId, default));

        Assert.Equal(("20261017-r2", "Debian GNU/Linux 13", "Microsoft.AspNetCore.App 10.0.0", "http://web:8080"),
            (detail.Run.RunKey, detail.OperatingSystem, detail.Runtime, detail.BaseUrl));
        Assert.Equal([new TestSuiteDto(WebSuite, TestType.Component, "Completed", 4, null), new TestSuiteDto(E2ESuite, TestType.EndToEnd, "Completed", 1, null)],
            detail.Suites);
        Assert.Equal(["CaseIntake.E2E.trx", "CaseIntake.Web.Tests.trx"], detail.Artifacts.Select(a => a.FileName).Order(StringComparer.Ordinal));
        Assert.All(detail.Artifacts, a => Assert.Equal(TestArtifactKind.ResultFile, a.Kind));
        Assert.Equal(("feature/Search_100%", "abc9999", "build-43"), (detail.Run.GitBranch, detail.Run.GitCommit, detail.Run.BuildId));
        Assert.Equal(TimeSpan.FromMinutes(3), detail.Run.Duration);

        TestResultsTestHarness.Fail(await Service.GetRunAsync(-1, default), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Suite_summaries_missing_fields_read_with_defaults()
    {
        var runId = await RunIdAsync("20261016-r1");
        await _h.QueryAsync(db => db.TestRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SuitesJson, """[{}, {"name": "S", "tests": 1.5}]""")));

        var detail = Ok(await Service.GetRunAsync(runId, default));

        Assert.Equal([new TestSuiteDto("", TestType.Unit, "", 0, null), new TestSuiteDto("S", TestType.Unit, "", 0, null)], detail.Suites);
    }

    [Fact]
    public async Task Executions_list_failures_first_and_trim_long_messages()
    {
        var runId = await RunIdAsync("20261016-r1");

        var page = Ok(await Service.ListExecutionsAsync(new TestExecutionQuery(RunId: runId), default));

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["Filters by status", "Was blocked", "Signs in with the keyboard", "Lists cases", "Removed later"],
            page.Items.Select(r => r.TestTitle));
        var failed = page.Items[0];
        Assert.Equal(301, failed.FailureMessage!.Length);
        Assert.EndsWith("…", failed.FailureMessage, StringComparison.Ordinal);
        Assert.Equal((TestOutcome.Failed, "Web.Tests", TestType.Component, Filters), (failed.Outcome, failed.Module, failed.Type, failed.FullName));
        Assert.Equal(1, page.Items.Single(r => r.Module == "E2E").ArtifactCount);
        Assert.Equal(TimeSpan.FromMilliseconds(125), failed.Duration);
    }

    [Fact]
    public async Task Executions_filter_by_outcome_module_type_and_search()
    {
        async Task<int> Count(TestExecutionQuery q) => Ok(await Service.ListExecutionsAsync(q, default)).TotalCount;

        Assert.Equal(10, await Count(new TestExecutionQuery()));
        Assert.Equal(3, await Count(new TestExecutionQuery(Outcomes: [TestOutcome.Failed, TestOutcome.Failed])));
        Assert.Equal(4, await Count(new TestExecutionQuery(Outcomes: [TestOutcome.Failed, TestOutcome.Blocked])));
        Assert.Equal(2, await Count(new TestExecutionQuery(Module: "E2E")));
        Assert.Equal(2, await Count(new TestExecutionQuery(Type: TestType.EndToEnd)));
        Assert.Equal(2, await Count(new TestExecutionQuery(Search: "FILTERS BY")));          // title
        Assert.Equal(2, await Count(new TestExecutionQuery(Search: "CaseListTests.Lists")));  // full name
        Assert.Equal(1, await Count(new TestExecutionQuery(RunId: await RunIdAsync("20261017-r2"), Outcomes: [TestOutcome.Passed], Module: "Web.Tests", Search: "new")));
        Assert.Equal(10, await Count(new TestExecutionQuery(Outcomes: [])));
    }

    [Fact]
    public async Task Execution_detail_has_the_case_steps_in_order_evidence_and_requirements()
    {
        var runId = await RunIdAsync("20261017-r2");
        var executionId = await _h.QueryAsync(db => db.TestExecutions.Where(e => e.RunId == runId && e.TestCase.FullName == Login).Select(e => e.Id).SingleAsync());

        var detail = Ok(await Service.GetExecutionAsync(executionId, default));

        Assert.Equal(("Signs in with the keyboard", "E5-S8", "20261017-r2"), (detail.TestCase.Title, detail.TestCase.Story, detail.Run.RunKey));
        Assert.Equal(("chromium", "390x844", TestOutcome.Passed), (detail.Browser, detail.Viewport, detail.Outcome));
        Assert.Equal([(1, "Submit"), (2, "Open")], detail.Steps.Select(s => (s.Order, s.Name)));
        Assert.Equal(TimeSpan.FromMilliseconds(30), detail.Steps[0].Duration);
        Assert.Equal([TestArtifactKind.Screenshot, TestArtifactKind.Trace], detail.Artifacts.Select(a => a.Kind));
        Assert.Equal(["R-4.2-11", "R-4.2-12"], detail.Requirements);
        Assert.NotNull(detail.StartedAtUtc);

        var plainId = await _h.QueryAsync(db => db.TestExecutions.Where(e => e.RunId == runId && e.TestCase.FullName == Lists).Select(e => e.Id).SingleAsync());
        var plain = Ok(await Service.GetExecutionAsync(plainId, default));
        Assert.Empty(plain.Requirements);
        Assert.Empty(plain.Steps);
        Assert.Equal("Expected 3 cases", plain.ErrorMessage);

        TestResultsTestHarness.Fail(await Service.GetExecutionAsync(-1, default), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Test_cases_list_failing_first_and_filter_by_module_type_last_outcome_and_search()
    {
        async Task<string[]> Titles(TestCaseQuery q) => [.. Ok(await Service.ListTestCasesAsync(q, default)).Items.Select(c => c.Title)];

        Assert.Equal(["Filters by status", "Lists cases", "Signs in with the keyboard", "Brand new", "Removed later", "Was blocked"], await Titles(new TestCaseQuery()));
        Assert.Equal(["Filters by status", "Lists cases"], await Titles(new TestCaseQuery(LastOutcome: TestOutcome.Failed)));
        Assert.Equal(["Signs in with the keyboard"], await Titles(new TestCaseQuery(Module: "E2E")));
        Assert.Equal(["Signs in with the keyboard"], await Titles(new TestCaseQuery(Type: TestType.EndToEnd)));
        Assert.Equal(["Signs in with the keyboard"], await Titles(new TestCaseQuery(Search: "e5-s8")));          // story
        Assert.Equal(["Signs in with the keyboard"], await Titles(new TestCaseQuery(Search: "R-4.2-12")));       // requirements
        Assert.Equal(["Brand new"], await Titles(new TestCaseQuery(Search: "brand")));                          // title
        Assert.Equal(["Removed later"], await Titles(new TestCaseQuery(Search: "CaseListTests.Removed")));      // full name
        var page = Ok(await Service.ListTestCasesAsync(new TestCaseQuery(Page: 3, PageSize: 2), default));
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(["Removed later", "Was blocked"], page.Items.Select(c => c.Title));
    }

    [Fact]
    public async Task Test_case_history_is_newest_first_and_bounded()
    {
        var caseId = await CaseIdAsync(Lists);

        var history = Ok(await Service.GetTestCaseHistoryAsync(caseId, 10, default));
        var latestOnly = Ok(await Service.GetTestCaseHistoryAsync(caseId, 0, default));   // clamped to at least one run

        Assert.Equal(("Lists cases", TestOutcome.Failed), (history.TestCase.Title, history.TestCase.LastOutcome));
        Assert.Equal([("20261017-r2", TestOutcome.Failed, "abc9999"), ("20261016-r1", TestOutcome.Passed, "abc1234def")],
            history.History.Select(p => (p.RunKey, p.Outcome, p.GitCommit)));
        Assert.Equal(TimeSpan.FromMilliseconds(125), history.History[0].Duration);
        Assert.Equal("20261017-r2", Assert.Single(latestOnly.History).RunKey);
        TestResultsTestHarness.Fail(await Service.GetTestCaseHistoryAsync(-1, 10, default), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Flaky_and_recurring_failures_are_found_in_recent_runs()
    {
        var flaky = Ok(await Service.GetFlakyTestsAsync(10, default));

        Assert.Equal(
        [
            ("Filters by status", 2, 0, 2, TestOutcome.Failed),
            ("Lists cases", 2, 1, 1, TestOutcome.Failed),
            ("Was blocked", 2, 1, 1, TestOutcome.Passed),
        ], flaky.Select(f => (f.Title, f.Runs, f.Passed, f.Failed, f.LastOutcome)));
        Assert.All(flaky, f => Assert.Equal("Web.Tests", f.Module));

        // At least two runs are always compared, so the newest run alone never decides.
        var lastTwo = Ok(await Service.GetFlakyTestsAsync(1, default));
        Assert.Equal(3, lastTwo.Count);
    }

    [Fact]
    public async Task Evidence_lists_filter_by_run_kind_and_search_across_file_caption_and_test()
    {
        async Task<TestArtifactListItemDto[]> List(TestArtifactQuery q) => [.. Ok(await Service.ListArtifactsAsync(q, default)).Items];
        var latest = await RunIdAsync("20261017-r2");

        Assert.Equal(7, (await List(new TestArtifactQuery())).Length);
        Assert.Equal(4, (await List(new TestArtifactQuery(RunId: latest))).Length);
        var shots = await List(new TestArtifactQuery(Kind: TestArtifactKind.Screenshot));
        Assert.Equal(2, shots.Length);
        Assert.All(shots, s => Assert.Equal(("Signs in with the keyboard", TestOutcome.Passed), (s.TestTitle, s.Outcome)));
        Assert.Equal(2, (await List(new TestArtifactQuery(Search: "after SIGN"))).Length);   // caption
        Assert.Equal(3, (await List(new TestArtifactQuery(Search: "keyboard"))).Length);     // test title
        var trx = await List(new TestArtifactQuery(RunId: latest, Search: "web.tests.trx"));  // file name
        var resultFile = Assert.Single(trx);
        Assert.Equal(("20261017-r2", (long?)null, (string?)null, (TestOutcome?)null), (resultFile.RunKey, resultFile.ExecutionId, resultFile.TestTitle, resultFile.Outcome));
        var paged = Ok(await Service.ListArtifactsAsync(new TestArtifactQuery(Page: 2, PageSize: 5), default));
        Assert.Equal((7, 2), (paged.TotalCount, paged.Items.Count));
    }

    [Fact]
    public async Task Modules_and_environments_are_distinct_and_sorted()
    {
        Assert.Equal(["E2E", "Web.Tests"], Ok(await Service.ListModulesAsync(default)));
        Assert.Equal(["docker compose tests", "nightly"], Ok(await Service.ListEnvironmentsAsync(default)));
    }

    [Fact]
    public async Task Opening_evidence_streams_the_file_and_audits_views_and_downloads()
    {
        var shot = await _h.QueryAsync(db => db.TestArtifacts.SingleAsync(a => a.Run.RunKey == "20261017-r2" && a.Kind == TestArtifactKind.Screenshot));

        var viewed = Ok(await Service.OpenArtifactAsync(shot.Id, download: false, default));
        await using (viewed.Content)
        {
            using var copy = new MemoryStream();
            await viewed.Content.CopyToAsync(copy);
            Assert.Equal(new byte[] { 4, 5, 6, 7 }, copy.ToArray());
        }
        var downloaded = Ok(await Service.OpenArtifactAsync(shot.Id, download: true, default));
        await downloaded.Content.DisposeAsync();

        Assert.Equal(("final.png", "image/png", 4L, TestArtifactKind.Screenshot), (viewed.FileName, viewed.ContentType, viewed.SizeBytes, viewed.Kind));
        var audit = await _h.QueryAsync(db => db.TestResultAuditEntries.Where(a => a.ArtifactId == shot.Id).OrderBy(a => a.Id).ToListAsync());
        Assert.Equal([("EvidenceViewed", "qa-id", "qualityanalyst@example.test", "Screenshot"), ("EvidenceDownloaded", "qa-id", "qualityanalyst@example.test", "Screenshot")],
            audit.Select(a => (a.Action, a.ActorId, a.Actor, a.Details)));
        Assert.All(audit, a => Assert.Equal(shot.RunId, a.RunId));
        var logs = _h.ServiceLogs.Collector.GetSnapshot().Where(r => r.Id.Id == 5203).ToList();
        Assert.Equal(["viewed", "downloaded"], logs.Select(r => r.GetStructuredStateValue("Access")));
    }

    [Fact]
    public async Task Purged_missing_and_unknown_evidence_cannot_be_opened()
    {
        var shots = await _h.QueryAsync(db => db.TestArtifacts.Where(a => a.Kind == TestArtifactKind.Screenshot).OrderBy(a => a.CreatedAtUtc).ToListAsync());
        await _h.QueryAsync(db => db.TestArtifacts.Where(a => a.Id == shots[0].Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Purged, true)));
        File.Delete(Path.Combine(_h.Root, shots[1].RelativePath));
        var escaping = await _h.QueryAsync(db => db.TestArtifacts.FirstAsync(a => a.Kind == TestArtifactKind.ResultFile));
        await _h.QueryAsync(db => db.TestArtifacts.Where(a => a.Id == escaping.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.RelativePath, "../outside.txt")));

        Assert.Equal("TestArtifactPurged", TestResultsTestHarness.Fail(await Service.OpenArtifactAsync(shots[0].Id, false, default), ErrorKind.NotFound).Code);
        Assert.Equal("TestArtifactNotFound", TestResultsTestHarness.Fail(await Service.OpenArtifactAsync(shots[1].Id, false, default), ErrorKind.NotFound).Code);
        Assert.Equal("TestArtifactNotFound", TestResultsTestHarness.Fail(await Service.OpenArtifactAsync(escaping.Id, true, default), ErrorKind.NotFound).Code);
        Assert.Equal("TestArtifactNotFound", TestResultsTestHarness.Fail(await Service.OpenArtifactAsync(Guid.NewGuid(), false, default), ErrorKind.NotFound).Code);

        Assert.Equal(2, _h.ServiceLogs.Collector.GetSnapshot().Count(r => r.Id.Id == 5206));
        Assert.Equal(0, await _h.QueryAsync(db => db.TestResultAuditEntries.CountAsync(a => a.Action.StartsWith("Evidence"))));
    }

    [Fact]
    public async Task A_run_exports_as_csv_or_html_and_each_export_is_audited()
    {
        var runId = await RunIdAsync("20261017-r2");

        var csv = Ok(await Service.ExportRunAsync(runId, "csv", default));
        var html = Ok(await Service.ExportRunAsync(runId, "html", default));

        Assert.Equal(("test-run-20261017-r2.csv", "text/csv; charset=utf-8"), (csv.FileName, csv.ContentType));
        var lines = Encoding.UTF8.GetString(csv.Content).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length);
        Assert.Contains("\"20261017-r2\",\"Web.Tests\",Component,\"Lists cases\",Failed,125,\"\",\"\",\"Expected 3 cases\"", lines);
        var text = Encoding.UTF8.GetString(html.Content);
        Assert.Contains("Previous run: 20261016-r1. Newly failed: 1. Newly fixed: 1. Still failing: 1.", text, StringComparison.Ordinal);
        Assert.Contains("<h3>Newly fixed</h3><ul><li>Was blocked</li></ul>", text, StringComparison.Ordinal);
        Assert.Contains("Generated 2026-10-16 15:00:00 UTC", text, StringComparison.Ordinal);
        Assert.Contains("docker compose tests (Debian GNU/Linux 13, Microsoft.AspNetCore.App 10.0.0)", text, StringComparison.Ordinal);

        var audit = await _h.QueryAsync(db => db.TestResultAuditEntries.Where(a => a.Action == "ReportExported").OrderBy(a => a.Id).ToListAsync());
        Assert.Equal([("csv", (long?)runId), ("html", (long?)runId)], audit.Select(a => (a.Details, a.RunId)));
        Assert.Equal(2, _h.ServiceLogs.Collector.GetSnapshot().Count(r => r.Id.Id == 5204));
    }

    [Fact]
    public async Task Unknown_formats_and_runs_cannot_be_exported()
    {
        var runId = await RunIdAsync("20261017-r2");

        Assert.Equal("UnknownFormat", TestResultsTestHarness.Fail(await Service.ExportRunAsync(runId, "pdf", default), ErrorKind.Validation).Code);
        Assert.Equal("UnknownFormat", TestResultsTestHarness.Fail(await Service.ExportRunAsync(runId, "CSV", default), ErrorKind.Validation).Code);
        TestResultsTestHarness.Fail(await Service.ExportRunAsync(-1, "csv", default), ErrorKind.NotFound);
        Assert.Equal(0, await _h.QueryAsync(db => db.TestResultAuditEntries.CountAsync(a => a.Action == "ReportExported")));
    }

    [Fact]
    public async Task Importing_through_the_service_needs_manage_and_records_the_actor()
    {
        _h.NewRun("20261018-r3", T.AddDays(2)).Suite(WebSuite, "Component", Trx.Document(new TrxTest("Ns.Web.CaseListTests", "Lists_cases"))).Complete();
        _h.Actor = TestResultsActors.ViewOnly;

        var denied = await Service.ImportAsync(default);
        _h.Actor = TestResultsActors.QualityAnalyst;
        var summary = Ok(await Service.ImportAsync(default));

        Assert.Equal("Forbidden", TestResultsTestHarness.Fail(denied, ErrorKind.Forbidden).Code);
        Assert.Equal((1, 3), (summary.Imported, summary.Skipped));
        var run = await _h.QueryAsync(db => db.TestRuns.SingleAsync(r => r.RunKey == "20261018-r3"));
        Assert.Equal("qualityanalyst@example.test", run.ImportedBy);
        var audit = await _h.QueryAsync(db => db.TestResultAuditEntries.SingleAsync(a => a.Details!.StartsWith("Run 20261018-r3")));
        Assert.Equal("qa-id", audit.ActorId);
        var deniedLog = Assert.Single(_h.ServiceLogs.Collector.GetSnapshot(), r => r.Id.Id == SecurityEvents.AuthorizationDeniedId);
        Assert.Equal((Permissions.TestResultsManage, "import"), (deniedLog.GetStructuredStateValue("Permission"), deniedLog.GetStructuredStateValue("ResourceId")));
    }

    [Fact]
    public async Task Every_call_without_the_view_permission_is_forbidden_and_logged()
    {
        _h.Actor = TestResultsActors.NoPermissions;
        var shotId = await _h.QueryAsync(db => db.TestArtifacts.Select(a => a.Id).FirstAsync());
        var runId = await RunIdAsync("20261017-r2");

        static async Task<string> Forbidden<T>(Task<Result<T>> call) => TestResultsTestHarness.Fail(await call, ErrorKind.Forbidden).Code;

        string[] codes =
        [
            await Forbidden(Service.GetDashboardAsync(default)),
            await Forbidden(Service.ListRunsAsync(new TestRunQuery(), default)),
            await Forbidden(Service.GetRunAsync(runId, default)),
            await Forbidden(Service.CompareWithPreviousAsync(runId, default)),
            await Forbidden(Service.ListExecutionsAsync(new TestExecutionQuery(), default)),
            await Forbidden(Service.GetExecutionAsync(1, default)),
            await Forbidden(Service.ListTestCasesAsync(new TestCaseQuery(), default)),
            await Forbidden(Service.GetTestCaseHistoryAsync(1, 10, default)),
            await Forbidden(Service.GetFlakyTestsAsync(10, default)),
            await Forbidden(Service.ListArtifactsAsync(new TestArtifactQuery(), default)),
            await Forbidden(Service.ListModulesAsync(default)),
            await Forbidden(Service.ListEnvironmentsAsync(default)),
            await Forbidden(Service.OpenArtifactAsync(shotId, true, default)),
            await Forbidden(Service.ExportRunAsync(runId, "csv", default)),
            await Forbidden(Service.ImportAsync(default)),
        ];

        Assert.All(codes, c => Assert.Equal("Forbidden", c));
        var denials = _h.ServiceLogs.Collector.GetSnapshot().Where(r => r.Id.Id == SecurityEvents.AuthorizationDeniedId).ToList();
        Assert.Equal(15, denials.Count);
        Assert.All(denials, d => Assert.Equal("nobody-id", d.GetStructuredStateValue("UserId")));
        Assert.Contains(denials, d => d.GetStructuredStateValue("ResourceId") == $"artifact {shotId}");
        Assert.Contains(denials, d => d.GetStructuredStateValue("ResourceId") == $"run {runId} export");
        Assert.Equal(0, await _h.QueryAsync(db => db.TestResultAuditEntries.CountAsync(a => a.Action != "RunImported")));
    }

    [Fact]
    public void Type_labels_name_the_tooling()
    {
        Assert.Equal(["Unit", "Component (bUnit)", "Integration", "End-to-end (Playwright)", "99"],
            new[] { TestType.Unit, TestType.Component, TestType.Integration, TestType.EndToEnd, (TestType)99 }.Select(TestResultsService.TypeLabel));
    }

    [Fact]
    public void A_summary_of_a_run_without_its_environment_reads_unknown()
    {
        var summary = TestResultsService.Summary(new TestRun { RunKey = "r", Runner = "x", Environment = null! });

        Assert.Equal("unknown", summary.Environment);
        Assert.Null(summary.Counts.PassRate);
    }
}
