using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using CaseIntake.Domain.TestResults;
using CaseIntake.IntegrationTests.Attachments;
using CaseIntake.TestKit;
using CaseIntake.TestResults.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>
/// E5: run folders written by scripts/run-tests.sh are imported once into the results tables, with redacted text,
/// hashed evidence referenced in place, and NotExecuted results for suites that did not run.
/// </summary>
[Trait("Category", "Story:E5-S2")]
public sealed class TestResultImporterTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>, IAsyncLifetime
{
    private const string WebSuite = "CaseIntake.Web.Tests";
    private const string E2ESuite = "CaseIntake.E2E";
    private const string LoginTest = "CaseIntake.E2E.LoginTests.Signs_in_with_the_keyboard";

    public Task InitializeAsync() => database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly byte[] Screenshot = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
    private static readonly byte[] TraceZip = [0x50, 0x4B, 3, 4, 9, 9];

    /// <summary>A complete run: a component suite with a pass, a failure and a skip, and a browser suite with evidence.</summary>
    private static RunFolder CompleteRun(TestResultsTestHarness h, string key = "20261016-150000-r1") =>
        h.NewRun(key)
            .Suite(WebSuite, "Component", Trx.Document(
                new TrxTest("Ns.Web.DashboardTests", "Dashboard_shows_the_latest_counts"),
                new TrxTest("Ns.Web.DashboardTests", "Dashboard_handles_errors", "Failed",
                    Message: "Veteran 900-12-3456 not found; password=hunter22",
                    StackTrace: "Authorization: Bearer abc.def.ghi\n   at Ns.Web.DashboardTests.Dashboard_handles_errors()",
                    StdOut: $"typed {TestResultsTestHarness.DemoPassword} into the password box"),
                new TrxTest("Ns.Web.DashboardTests", "Skipped_until_issue_9", "NotExecuted")))
            .Suite(E2ESuite, "EndToEnd", Trx.Document(new TrxTest("CaseIntake.E2E.LoginTests", "Signs_in_with_the_keyboard")))
            .Catalog(WebSuite, ("Ns.Web.DashboardTests.Dashboard_shows_the_latest_counts", ["Story:E5-S1", "Req:R-1", "Req: R-2", "Priority:High"]))
            .Evidence("login-keyboard", new EvidenceManifest
            {
                FullName = LoginTest,
                Title = "Signs in with the keyboard",
                Story = "E5-S8",
                Requirements = ["R-4.2-11", "R-4.2-12"],
                AcceptanceCriteria = "Every control is reachable by Tab.",
                Priority = "High",
                Description = "Keyboard-only sign in.",
                Preconditions = "Seeded demo users.",
                Expected = "The dashboard opens.",
                Actual = "The dashboard opened for 900-00-0001.",
                Browser = "chromium",
                Viewport = "1440x900",
                Steps =
                [
                    new EvidenceStep { Name = "Open the sign-in page", DurationMs = 120, Expected = "Form shown", Actual = "Form shown" },
                    new EvidenceStep { Name = $"Type {TestResultsTestHarness.DemoPassword}", Outcome = "notanoutcome", DurationMs = -5, Message = "token=abc123" },
                ],
                Artifacts =
                [
                    new EvidenceFile { Kind = "Screenshot", File = "final.png", Caption = "Dashboard after sign in" },
                    new EvidenceFile { Kind = "Trace", File = "trace.zip" },
                ],
                ConsoleErrors = ["Failed to load resource for 900-12-3456"],
                NetworkFailures = ["GET /api/x 500 Cookie: CaseIntake.Auth=abc"],
            }, ("final.png", Screenshot), ("trace.zip", TraceZip))
            .ServerLog("server-web-e2e.log", "GET /cases?ssn=900123456 200\nCookie: CaseIntake.Auth=CfDJ8secret\nplain line\n");

    [Fact]
    public async Task A_complete_run_is_imported_with_counts_cases_executions_steps_and_hashed_evidence()
    {
        using var h = new TestResultsTestHarness(database);
        var folder = CompleteRun(h).Complete();

        var summary = await h.ImportAsync();

        Assert.Equal((1, 0, 4, 5), (summary.Imported, summary.Skipped, summary.Executions, summary.Artifacts));
        Assert.Empty(summary.Problems);

        await using var db = database.CreateContext();
        var run = await db.TestRuns.Include(r => r.Environment).SingleAsync();
        Assert.Equal(("20261016-150000-r1", TestRunStatus.Completed), (run.RunKey, run.Status));
        Assert.Equal((4, 2, 1, 1, 0, 0), (run.Total, run.Passed, run.Failed, run.Skipped, run.Blocked, run.NotExecuted));
        Assert.Equal(180_000, run.DurationMs);
        Assert.Equal(("main", "abc1234def", "build-42"), (run.GitBranch, run.GitCommit, run.BuildId));
        Assert.Equal(("docker compose tests", "Debian GNU/Linux 13", "http://web:8080"),
            (run.Environment.Name, run.Environment.OperatingSystem, run.Environment.BaseUrl));
        Assert.Equal(Path.Combine("runs", "20261016-150000-r1"), run.SourcePath);
        Assert.Equal(("qa@example.test", TestResultsTestHarness.StartTime), (run.ImportedBy, run.ImportedAtUtc));
        using (var suites = JsonDocument.Parse(run.SuitesJson))
        {
            Assert.Equal([(WebSuite, "Component", "Completed", 3), (E2ESuite, "EndToEnd", "Completed", 1)],
                suites.RootElement.EnumerateArray().Select(s => (s.GetProperty("name").GetString(), s.GetProperty("type").GetString(),
                    s.GetProperty("status").GetString(), s.GetProperty("tests").GetInt32())).ToList());
        }

        var counts = await db.TestCases.SingleAsync(c => c.FullName == "Ns.Web.DashboardTests.Dashboard_shows_the_latest_counts");
        Assert.Equal(("Dashboard shows the latest counts", "Web.Tests", TestType.Component), (counts.Title, counts.Module, counts.Type));
        Assert.Equal(("E5-S1", "R-1, R-2", "High"), (counts.Story, counts.Requirements, counts.Priority));
        Assert.Equal("Ns.Web.DashboardTests.Dashboard_shows_the_latest_counts", counts.Description);
        Assert.Equal((TestOutcome.Passed, run.Id), (counts.LastOutcome, counts.LastRunId));
        Assert.Equal(TestResultImporter.Key(counts.FullName), counts.Key);
        Assert.Equal(TestResultsTestHarness.StartTime, counts.FirstSeenAtUtc);

        var login = await db.TestCases.SingleAsync(c => c.FullName == LoginTest);
        Assert.Equal(("Signs in with the keyboard", "E2E", TestType.EndToEnd, "E5-S8", "R-4.2-11, R-4.2-12", "High"),
            (login.Title, login.Module, login.Type, login.Story, login.Requirements, login.Priority));
        Assert.Equal(("Keyboard-only sign in.", "Every control is reachable by Tab.", "Seeded demo users.", "The dashboard opens."),
            (login.Description, login.AcceptanceCriteria, login.Preconditions, login.ExpectedResult));

        var execution = await db.TestExecutions.Include(e => e.Steps).Include(e => e.Artifacts).SingleAsync(e => e.TestCaseId == login.Id);
        Assert.Equal(("chromium", "1440x900", TestOutcome.Passed, 125L), (execution.Browser, execution.Viewport, execution.Outcome, execution.DurationMs));
        Assert.Equal("The dashboard opened for [REDACTED-SSN].", execution.ActualResult);
        Assert.Equal("Failed to load resource for [REDACTED-SSN]", execution.ConsoleLog);
        Assert.Equal("GET /api/x 500 Cookie: [REDACTED]", execution.NetworkLog);
        var steps = execution.Steps.OrderBy(s => s.Order).ToList();
        Assert.Equal([(1, "Open the sign-in page", TestOutcome.Passed, 120L), (2, "Type [REDACTED]", TestOutcome.Failed, 0L)],
            steps.Select(s => (s.Order, s.Name, s.Outcome, s.DurationMs)).ToList());
        Assert.Equal(("Form shown", "Form shown", "token=[REDACTED]"), (steps[0].Expected, steps[0].Actual, steps[1].Message));

        var shot = Assert.Single(execution.Artifacts, a => a.Kind == TestArtifactKind.Screenshot);
        Assert.Equal(("final.png", "image/png", 7L, "Dashboard after sign in"), (shot.FileName, shot.ContentType, shot.SizeBytes, shot.Caption));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Screenshot)), shot.Sha256);
        Assert.Equal("runs/20261016-150000-r1/evidence/login-keyboard/final.png", shot.RelativePath);
        Assert.Equal(run.Id, shot.RunId);
        var trace = Assert.Single(execution.Artifacts, a => a.Kind == TestArtifactKind.Trace);
        Assert.Equal(("application/zip", (string?)null), (trace.ContentType, trace.Caption));

        var runLevel = await db.TestArtifacts.Where(a => a.ExecutionId == null).OrderBy(a => a.FileName).ToListAsync();
        Assert.Equal([("CaseIntake.E2E.trx", TestArtifactKind.ResultFile, "application/xml"), ("CaseIntake.Web.Tests.trx", TestArtifactKind.ResultFile, "application/xml"),
                      ("server-web-e2e.redacted.log", TestArtifactKind.ServerLog, "text/plain")],
            runLevel.Select(a => (a.FileName, a.Kind, a.ContentType)).ToList());
        Assert.Equal("CaseIntake.Web.Tests results (TRX)", runLevel[1].Caption);

        var audit = await db.TestResultAuditEntries.SingleAsync();
        Assert.Equal(("RunImported", "qa-id", "qa@example.test", "Run 20261016-150000-r1: 4 results, 5 evidence files"),
            (audit.Action, audit.ActorId, audit.Actor, audit.Details));
        var log = Assert.Single(h.ImporterLogs.Collector.GetSnapshot(), r => r.Id.Id == 5201);
        Assert.Equal("4", log.GetStructuredStateValue("Total"));
        Assert.True(File.Exists(folder.PathOf("run.json")), "import must leave the run folder in place");
    }

    [Fact]
    public async Task Imported_text_is_redacted_and_only_a_redacted_copy_of_the_server_log_is_kept()
    {
        using var h = new TestResultsTestHarness(database);
        var folder = CompleteRun(h).Complete();

        await h.ImportAsync();

        await using var db = database.CreateContext();
        var failed = await db.TestExecutions.SingleAsync(e => e.Outcome == TestOutcome.Failed);
        Assert.Equal("Veteran [REDACTED-SSN] not found; password=[REDACTED]", failed.ErrorMessage);
        Assert.StartsWith("Authorization: Bearer [REDACTED]\n", failed.StackTrace, StringComparison.Ordinal);
        Assert.Equal("typed [REDACTED] into the password box", failed.StandardOutput);
        var stored = string.Join("\n", await db.TestExecutions.Select(e => (e.ErrorMessage ?? "") + (e.StackTrace ?? "") + (e.StandardOutput ?? "")
            + (e.ConsoleLog ?? "") + (e.NetworkLog ?? "") + (e.ActualResult ?? "")).ToListAsync());
        Assert.DoesNotContain("900-12-3456", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(TestResultsTestHarness.DemoPassword, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter22", stored, StringComparison.Ordinal);

        Assert.False(File.Exists(folder.PathOf("server-web-e2e.log")), "the raw server log must be removed");
        var redacted = await File.ReadAllTextAsync(folder.PathOf("server-web-e2e.redacted.log"));
        Assert.Equal("GET /cases?ssn=[REDACTED-SSN] 200\nCookie: [REDACTED]\nplain line\n", redacted);
        var artifact = await db.TestArtifacts.SingleAsync(a => a.Kind == TestArtifactKind.ServerLog);
        Assert.Equal(("Server log (redacted)", "runs/20261016-150000-r1/server-web-e2e.redacted.log"), (artifact.Caption, artifact.RelativePath));
    }

    [Fact]
    public async Task Importing_again_skips_runs_already_imported_and_changes_nothing()
    {
        using var h = new TestResultsTestHarness(database);
        CompleteRun(h).Complete();
        await h.ImportAsync();

        var again = await h.ImportAsync();

        Assert.Equal((0, 1, 0, 0), (again.Imported, again.Skipped, again.Executions, again.Artifacts));
        Assert.Empty(again.Problems);
        await using var db = database.CreateContext();
        Assert.Equal((1, 4, 4, 5, 1), (await db.TestRuns.CountAsync(), await db.TestCases.CountAsync(), await db.TestExecutions.CountAsync(),
            await db.TestArtifacts.CountAsync(), await db.TestResultAuditEntries.CountAsync()));
    }

    [Fact]
    public async Task A_folder_still_being_written_is_ignored_until_its_run_json_appears()
    {
        using var h = new TestResultsTestHarness(database);
        var folder = h.NewRun("20261016-150000-pending").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")));
        File.WriteAllText(folder.PathOf("run.json.pending"), "{}");

        var first = await h.ImportAsync();
        folder.Complete();
        var second = await h.ImportAsync();

        Assert.Equal((0, 0), (first.Imported, first.Skipped));
        Assert.Equal(1, second.Imported);
    }

    [Fact]
    public async Task Without_a_runs_folder_the_import_explains_that_nothing_has_been_written()
    {
        using var h = new TestResultsTestHarness(database);

        var summary = await h.ImportAsync();

        Assert.Equal((0, 0, 0, 0), (summary.Imported, summary.Skipped, summary.Executions, summary.Artifacts));
        Assert.Equal("No test results have been written yet. Run the documented test command first.", Assert.Single(summary.Problems));
    }

    [Fact]
    public async Task A_suite_that_failed_to_start_records_every_known_test_of_its_module_as_not_executed()
    {
        using var h = new TestResultsTestHarness(database);
        CompleteRun(h, "20261016-150000-r1").Complete();
        await h.ImportAsync();

        h.NewRun("20261017-150000-r2", TestResultsTestHarness.StartTime.AddDays(1))
            .Suite(WebSuite, "Component", trx: null, status: "FailedToStart", message: "dotnet test exited with code 1 before writing results.")
            .Complete();
        var summary = await h.ImportAsync();

        Assert.Equal((1, 1, 3), (summary.Imported, summary.Skipped, summary.Executions));
        await using var db = database.CreateContext();
        var run = await db.TestRuns.SingleAsync(r => r.RunKey == "20261017-150000-r2");
        Assert.Equal((TestRunStatus.FailedToStart, 3, 3, 0), (run.Status, run.Total, run.NotExecuted, run.Passed));
        var executions = await db.TestExecutions.Where(e => e.RunId == run.Id).ToListAsync();
        Assert.All(executions, e =>
        {
            Assert.Equal(TestOutcome.NotExecuted, e.Outcome);
            Assert.Equal("Not executed: suite CaseIntake.Web.Tests failed to start. dotnet test exited with code 1 before writing results.", e.ErrorMessage);
        });
        var webCases = await db.TestCases.Where(c => c.Module == "Web.Tests").ToListAsync();
        Assert.All(webCases, c => Assert.Equal((TestOutcome.NotExecuted, run.Id), (c.LastOutcome, c.LastRunId)));
        var login = await db.TestCases.SingleAsync(c => c.Module == "E2E");
        Assert.Equal(TestOutcome.Passed, login.LastOutcome);   // the E2E suite was not part of this run
    }

    [Fact]
    public async Task A_run_where_one_suite_produced_no_results_is_partial_and_never_reads_as_passing()
    {
        using var h = new TestResultsTestHarness(database);
        CompleteRun(h, "20261016-150000-r1").Complete();
        await h.ImportAsync();

        h.NewRun("20261017-150000-r2", TestResultsTestHarness.StartTime.AddDays(1))
            .Suite(WebSuite, "Component", Trx.Document(new TrxTest("Ns.Web.DashboardTests", "Dashboard_shows_the_latest_counts")))
            .Suite(E2ESuite, "EndToEnd", trx: null, status: "NoResults", message: "Zero tests discovered.")
            .Complete();
        h.NewRun("20261018-150000-r3", TestResultsTestHarness.StartTime.AddDays(2))
            .Suite(WebSuite, "Component", Trx.Document(new TrxTest("Ns.Web.DashboardTests", "Dashboard_shows_the_latest_counts")))
            .SuiteReferencing(E2ESuite, "EndToEnd", "CaseIntake.E2E.trx")   // reported Completed, but the TRX file is missing
            .Complete();
        await h.ImportAsync();

        await using var db = database.CreateContext();
        var r2 = await db.TestRuns.SingleAsync(r => r.RunKey == "20261017-150000-r2");
        Assert.Equal((TestRunStatus.Partial, 2, 1, 1), (r2.Status, r2.Total, r2.Passed, r2.NotExecuted));
        var notRun = await db.TestExecutions.SingleAsync(e => e.RunId == r2.Id && e.Outcome == TestOutcome.NotExecuted);
        Assert.Equal("Not executed: suite CaseIntake.E2E produced no results. Zero tests discovered.", notRun.ErrorMessage);
        Assert.Equal("NoResults", SuiteStatuses(r2.SuitesJson)[E2ESuite]);

        var r3 = await db.TestRuns.SingleAsync(r => r.RunKey == "20261018-150000-r3");
        Assert.Equal((TestRunStatus.Partial, 1), (r3.Status, r3.NotExecuted));
        Assert.Equal("NoResults", SuiteStatuses(r3.SuitesJson)[E2ESuite]);
    }

    [Fact]
    public async Task A_run_the_runner_reports_as_partial_or_failed_to_start_keeps_that_status()
    {
        using var h = new TestResultsTestHarness(database);
        var partial = h.NewRun("r-partial").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")));
        partial.Manifest.Status = "partial";
        partial.Complete();
        var failed = h.NewRun("r-failed").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")));
        failed.Manifest.Status = "FailedToStart";
        failed.Complete();
        var empty = h.NewRun("r-empty");
        empty.Manifest.Environment = null;
        empty.Manifest.Git = null;
        empty.Manifest.FinishedAtUtc = null;
        empty.Manifest.Notes = "Synthetic run for 900-12-3456";
        empty.Complete();

        await h.ImportAsync();

        await using var db = database.CreateContext();
        var runs = await db.TestRuns.Include(r => r.Environment).ToDictionaryAsync(r => r.RunKey);
        Assert.Equal(TestRunStatus.Partial, runs["r-partial"].Status);
        Assert.Equal(TestRunStatus.FailedToStart, runs["r-failed"].Status);
        Assert.Equal(TestRunStatus.Completed, runs["r-empty"].Status);
        Assert.Equal((0L, (string?)null, "unknown"), (runs["r-empty"].DurationMs, runs["r-empty"].GitBranch, runs["r-empty"].Environment.Name));
        Assert.Equal("Synthetic run for [REDACTED-SSN]", runs["r-empty"].Notes);
        Assert.Equal(runs["r-partial"].EnvironmentId, runs["r-failed"].EnvironmentId);   // the same environment is stored once
        Assert.Equal(2, await db.TestEnvironments.CountAsync());
    }

    [Fact]
    public async Task An_older_run_imported_later_does_not_replace_the_last_outcome_from_a_newer_run()
    {
        using var h = new TestResultsTestHarness(database);
        h.NewRun("b-newer", TestResultsTestHarness.StartTime.AddDays(1))
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.Web.ListTests", "Lists_cases", "Failed", Message: "boom")))
            .Complete();
        await h.ImportAsync();
        h.NewRun("a-older", TestResultsTestHarness.StartTime)
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.Web.ListTests", "Lists_cases"), new TrxTest("Ns.Web.ListTests", "Only_in_the_old_run")))
            .Complete();

        await h.ImportAsync();

        await using var db = database.CreateContext();
        var newer = await db.TestRuns.SingleAsync(r => r.RunKey == "b-newer");
        var older = await db.TestRuns.SingleAsync(r => r.RunKey == "a-older");
        var lists = await db.TestCases.SingleAsync(c => c.FullName == "Ns.Web.ListTests.Lists_cases");
        Assert.Equal((TestOutcome.Failed, newer.Id), (lists.LastOutcome, lists.LastRunId));
        var onlyOld = await db.TestCases.SingleAsync(c => c.FullName == "Ns.Web.ListTests.Only_in_the_old_run");
        Assert.Equal((TestOutcome.Passed, older.Id), (onlyOld.LastOutcome, onlyOld.LastRunId));
        Assert.Equal(2, await db.TestExecutions.CountAsync(e => e.TestCaseId == lists.Id));
    }

    [Fact]
    public async Task Catalog_traits_and_trx_categories_describe_a_test_and_later_runs_keep_what_they_do_not_repeat()
    {
        using var h = new TestResultsTestHarness(database);
        h.NewRun("r1")
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.Web.T", "Traits_from_trx", Categories: ["story: E5-S4", "Req:R-9", "Priority:Low"])))
            .Complete();
        await h.ImportAsync();
        h.NewRun("r2", TestResultsTestHarness.StartTime.AddDays(1))
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.Web.T", "Traits_from_trx")))
            .Complete();

        await h.ImportAsync();

        await using var db = database.CreateContext();
        var testCase = await db.TestCases.SingleAsync();
        Assert.Equal(("E5-S4", "R-9", "Low", "Traits from trx"), (testCase.Story, testCase.Requirements, testCase.Priority, testCase.Title));
        Assert.Equal(TestResultsTestHarness.StartTime, testCase.FirstSeenAtUtc);
    }

    [Fact]
    public async Task Broken_or_unsafe_run_folders_are_reported_without_stopping_the_others()
    {
        using var h = new TestResultsTestHarness(database);
        h.NewRun("_unsafe-name").Complete();
        var mismatch = h.NewRun("r-mismatch");
        mismatch.Manifest.RunKey = "something-else";
        mismatch.Complete();
        File.WriteAllText(h.NewRun("r-bad-json").PathOf("run.json"), "{ not json");
        File.WriteAllText(h.NewRun("r-null-json").PathOf("run.json"), "null");
        using (var big = File.Create(h.NewRun("r-huge-manifest").PathOf("run.json")))
        {
            big.SetLength(1024 * 1024 + 1);
        }
        h.NewRun("r-bad-trx").Suite(WebSuite, "Unit", "<TestRun><Results>").Complete();
        CompleteRun(h, "r-good").Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(1, summary.Imported);
        Assert.Equal(
        [
            "_unsafe-name: could not be imported (InvalidDataException).",
            "r-bad-json: could not be imported (JsonException).",
            "r-bad-trx: could not be imported (XmlException).",
            "r-huge-manifest: could not be imported (InvalidDataException).",
            "r-mismatch: could not be imported (InvalidDataException).",
            "r-null-json: could not be imported (InvalidDataException).",
        ], summary.Problems);
        Assert.Equal(6, h.ImporterLogs.Collector.GetSnapshot().Count(r => r.Id.Id == 5202));
        await using var db = database.CreateContext();
        Assert.Equal("r-good", (await db.TestRuns.SingleAsync()).RunKey);
    }

    [Fact]
    public async Task Evidence_outside_its_folder_missing_files_and_unreadable_manifests_are_reported_and_skipped()
    {
        using var h = new TestResultsTestHarness(database, o => o.MaxArtifactBytes = 6);
        var folder = h.NewRun("r-evidence")
            .Suite(E2ESuite, "EndToEnd", Trx.Document(new TrxTest("CaseIntake.E2E.LoginTests", "Signs_in_with_the_keyboard")))
            .SuiteReferencing("CaseIntake.Escape", "Unit", "../outside.trx")
            .SuiteReferencing("CaseIntake.Rooted", "Unit", "/etc/hosts")
            .Evidence("login", new EvidenceManifest
            {
                FullName = LoginTest,
                Artifacts =
                [
                    new EvidenceFile { Kind = "Video", File = "../../run.json" },
                    new EvidenceFile { Kind = "Screenshot", File = "missing.png" },
                    new EvidenceFile { Kind = "Hologram", File = "big.bin" },
                    new EvidenceFile { Kind = "ConsoleLog", File = "console.txt" },
                ],
            }, ("big.bin", new byte[7]), ("console.txt", new byte[6]))
            .Evidence("no-name", new EvidenceManifest { FullName = "" })
            .Catalog("Broken", [])
            .ServerLog("present.log", "ok");
        folder.Manifest.ServerLogs.Add("missing.log");
        folder.Manifest.ServerLogs.Add("../escape.log");
        Directory.CreateDirectory(folder.PathOf("evidence", "no-manifest"));
        Directory.CreateDirectory(folder.PathOf("evidence", "broken"));
        File.WriteAllText(folder.PathOf("evidence", "broken", "evidence.json"), "{ nope");
        File.WriteAllText(folder.PathOf("catalog", "Broken.json"), "[{ nope");
        folder.Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(1, summary.Imported);
        var problems = summary.Problems.Select(p => p["r-evidence: ".Length..]).ToList();
        Assert.Contains("An evidence manifest could not be read.", problems);
        Assert.Contains("A test catalog could not be read.", problems);
        Assert.Equal(2, problems.Count(p => p == $"{E2ESuite}: evidence file missing or outside its folder."));
        Assert.Contains("Evidence file big.bin is larger than the import limit and was skipped.", problems);
        Assert.Contains("Evidence file CaseIntake.E2E.trx is larger than the import limit and was skipped.", problems);
        Assert.Equal(2, problems.Count(p => p == "A listed server log is missing."));

        await using var db = database.CreateContext();
        var artifacts = await db.TestArtifacts.OrderBy(a => a.FileName).ToListAsync();
        Assert.Equal([("console.txt", TestArtifactKind.ConsoleLog, "text/plain"), ("present.redacted.log", TestArtifactKind.ServerLog, "text/plain")],
            artifacts.Select(a => (a.FileName, a.Kind, a.ContentType)).ToList());
        var run = await db.TestRuns.SingleAsync();
        Assert.Equal("NoResults", SuiteStatuses(run.SuitesJson)["CaseIntake.Escape"]);
    }

    [Fact]
    public async Task Unknown_evidence_kinds_fall_back_to_screenshots()
    {
        using var h = new TestResultsTestHarness(database);
        h.NewRun("r-kinds")
            .Suite(E2ESuite, "NotAType", Trx.Document(new TrxTest("CaseIntake.E2E.LoginTests", "Signs_in_with_the_keyboard")))
            .Evidence("login", new EvidenceManifest
            {
                FullName = LoginTest,
                Artifacts = [new EvidenceFile { Kind = "Hologram", File = "odd.webm" }],
            }, ("odd.webm", [1, 2]))
            .Complete();

        await h.ImportAsync();

        await using var db = database.CreateContext();
        var artifact = await db.TestArtifacts.SingleAsync(a => a.ExecutionId != null);
        Assert.Equal((TestArtifactKind.Screenshot, "video/webm"), (artifact.Kind, artifact.ContentType));
        Assert.Equal(TestType.Unit, (await db.TestCases.SingleAsync()).Type);
    }

    [Fact]
    public async Task Oversized_result_files_and_server_logs_are_not_read()
    {
        using var h = new TestResultsTestHarness(database);
        var folder = h.NewRun("r-oversized").SuiteReferencing(WebSuite, "Unit", "CaseIntake.Web.Tests.trx");
        using (var trx = File.Create(folder.PathOf("CaseIntake.Web.Tests.trx")))
        {
            trx.SetLength(256L * 1024 * 1024 + 1);   // sparse: only the length is checked
        }
        using (var log = File.Create(folder.PathOf("server.log")))
        {
            log.SetLength(20L * 1024 * 1024 + 1);
        }
        folder.Manifest.ServerLogs.Add("server.log");
        folder.Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(1, summary.Imported);
        Assert.Equal(["r-oversized: CaseIntake.Web.Tests: result file too large to import.", "r-oversized: A server log is too large to import."], summary.Problems);
        Assert.True(File.Exists(folder.PathOf("server.log")), "an oversized log is left untouched");
        await using var db = database.CreateContext();
        Assert.Equal(0, await db.TestArtifacts.CountAsync());
    }

    [Fact]
    public async Task A_server_log_that_cannot_be_redacted_is_never_shown()
    {
        if (OperatingSystem.IsWindows())
        {
            return;   // Unix file modes are used to make the run folder read-only; the tests service runs on Linux.
        }
        using var h = new TestResultsTestHarness(database);
        var folder = h.NewRun("r-readonly")
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")))
            .ServerLog("server.log", "Cookie: CaseIntake.Auth=secret")
            .Complete();
        var mode = File.GetUnixFileMode(folder.Path);
        File.SetUnixFileMode(folder.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Contracts.TestResults.ImportSummaryDto summary;
        try
        {
            summary = await h.ImportAsync();
        }
        finally
        {
            File.SetUnixFileMode(folder.Path, mode);
        }

        Assert.Equal(1, summary.Imported);
        Assert.Equal(["r-readonly: A server log could not be redacted (UnauthorizedAccessException); it is not shown."], summary.Problems);
        await using var db = database.CreateContext();
        Assert.False(await db.TestArtifacts.AnyAsync(a => a.Kind == TestArtifactKind.ServerLog));
        Assert.Equal(1, await db.TestArtifacts.CountAsync(a => a.Kind == TestArtifactKind.ResultFile));
    }

    [Fact]
    public async Task A_second_result_for_the_same_test_in_one_run_is_ignored()
    {
        using var h = new TestResultsTestHarness(database);
        h.NewRun("r-dup")
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.Shared", "Same_test")))
            .Suite("CaseIntake.Other.Tests", "Unit", Trx.Document(new TrxTest("Ns.Shared", "Same_test", "Failed")))
            .Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(["r-dup: CaseIntake.Other.Tests: a second result for the same test in this run was ignored."], summary.Problems);
        await using var db = database.CreateContext();
        var execution = await db.TestExecutions.SingleAsync();
        Assert.Equal(TestOutcome.Passed, execution.Outcome);
    }

    [Fact]
    public async Task A_save_failure_is_reported_and_the_run_is_imported_on_a_later_pass()
    {
        var faults = new SaveFaults { FailBeforeCommit = true };
        using var h = new TestResultsTestHarness(database, null, faults);
        h.NewRun("r-save").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B"))).Complete();

        var failed = await h.ImportAsync();
        faults.FailBeforeCommit = false;
        var retried = await h.ImportAsync();

        Assert.Equal(0, failed.Imported);
        Assert.Equal(["r-save: not saved (DbUpdateException)."], failed.Problems);
        Assert.Equal(1, retried.Imported);
        Assert.Single(h.ImporterLogs.Collector.GetSnapshot(), r => r.Id.Id == 5202);
    }

    [Fact]
    public async Task A_run_imported_by_another_instance_during_the_pass_is_skipped()
    {
        var race = new ImportedElsewhere(database);
        using var h = new TestResultsTestHarness(database, null, race);
        h.NewRun("r-race").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B"))).Complete();
        race.RunKey = "r-race";

        var summary = await h.ImportAsync();

        Assert.Equal((0, 1, 0), (summary.Imported, summary.Skipped, summary.Executions));
        Assert.Empty(summary.Problems);
        await using var db = database.CreateContext();
        Assert.Equal("another instance", (await db.TestRuns.SingleAsync()).ImportedBy);
        Assert.Equal(0, await db.TestExecutions.CountAsync());
    }

    [Theory]
    [InlineData("Ns.Class.Some_behaviour_works(x: 1)", "Some behaviour works (x: 1)")]
    [InlineData("Ns.Class.lower_case", "Lower case")]
    [InlineData("NoNamespace", "NoNamespace")]
    [InlineData("Ns.Class._", "Ns.Class._")]
    public void Test_names_are_humanized_into_titles(string fullName, string title)
    {
        Assert.Equal(title, TestResultImporter.Humanize(Result(fullName)));
    }

    [Fact]
    public void Humanized_titles_are_capped_to_the_title_column()
    {
        var title = TestResultImporter.Humanize(Result("Ns.C." + new string('a', 500)));

        Assert.Equal(TestResultFieldLimits.NameMaxLength, title.Length);
    }

    [Fact]
    public void Safe_child_paths_stay_inside_their_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "run");

        Assert.Equal(Path.Combine(folder, "a.trx"), TestResultImporter.SafeChild(folder, "a.trx"));
        Assert.Equal(Path.Combine(folder, "a.trx"), TestResultImporter.SafeChild(folder + Path.DirectorySeparatorChar, Path.Combine("sub", "..", "a.trx")));
        Assert.Null(TestResultImporter.SafeChild(folder, Path.Combine("..", "x")));
        Assert.Null(TestResultImporter.SafeChild(folder, Path.Combine("..", "run-evil", "x")));
        Assert.Null(TestResultImporter.SafeChild(folder, Path.GetFullPath(Path.Combine(folder, "a.trx"))));
        Assert.Null(TestResultImporter.SafeChild(folder, ""));
        Assert.Null(TestResultImporter.SafeChild(folder, "   "));
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".JPG", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".webm", "video/webm")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".zip", "application/zip")]
    [InlineData(".json", "application/json")]
    [InlineData(".trx", "application/xml")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".html", "text/html")]
    [InlineData(".htm", "text/html")]
    [InlineData(".log", "text/plain")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".exe", "application/octet-stream")]
    [InlineData("", "application/octet-stream")]
    public void Evidence_content_types_follow_the_extension(string extension, string contentType)
    {
        Assert.Equal(contentType, TestResultImporter.ContentTypeFor(extension));
    }

    [Fact]
    public void Names_types_outcomes_and_kinds_parse_with_safe_fallbacks()
    {
        Assert.Equal("Web.Tests", TestResultImporter.ModuleName("CaseIntake.Web.Tests"));
        Assert.Equal("Other.Tests", TestResultImporter.ModuleName("Other.Tests"));
        Assert.Equal("unknown", TestResultImporter.ModuleName("CaseIntake."));
        Assert.Equal(TestType.EndToEnd, TestResultImporter.ParseType("endtoend"));
        Assert.Equal(TestType.Unit, TestResultImporter.ParseType("42"));
        Assert.Equal(TestType.Unit, TestResultImporter.ParseType(null));
        Assert.Equal(TestOutcome.Blocked, TestResultImporter.ParseOutcome("blocked"));
        Assert.Equal(TestOutcome.Failed, TestResultImporter.ParseOutcome("7"));
        Assert.Equal(TestArtifactKind.NetworkLog, TestResultImporter.ParseKind("networklog"));
        Assert.Equal(TestArtifactKind.Screenshot, TestResultImporter.ParseKind(null));
        Assert.Equal(64, TestResultImporter.Key("x").Length);
    }

    private static Dictionary<string, string> SuiteStatuses(string suitesJson)
    {
        using var doc = JsonDocument.Parse(suitesJson);
        return doc.RootElement.EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("status").GetString()!);
    }

    private static TrxResult Result(string fullName) =>
        new(fullName, "Ns.Class", fullName, TestOutcome.Passed, "Passed", null, null, TimeSpan.Zero, null, null, null, []);

    /// <summary>Inserts the run from a second connection just before the importer re-checks its run key.</summary>
    private sealed class ImportedElsewhere(PostgresDatabaseFixture database) : DbCommandInterceptor
    {
        public string? RunKey { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (RunKey is { } key && command.CommandText.Contains("EXISTS", StringComparison.Ordinal))
            {
                RunKey = null;
                await using var other = database.CreateContext();
                other.TestRuns.Add(new TestRun
                {
                    RunKey = key, Runner = "other", ImportedBy = "another instance", SourcePath = "runs/" + key,
                    StartedAtUtc = TestResultsTestHarness.StartTime, ImportedAtUtc = TestResultsTestHarness.StartTime,
                    Environment = new TestEnvironment { Key = new string('e', 64), Name = "other", OperatingSystem = "os", Runtime = "rt" },
                });
                await other.SaveChangesAsync(cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
