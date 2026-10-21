using CaseIntake.Domain.TestResults;
using CaseIntake.IntegrationTests.Attachments;
using CaseIntake.TestKit;
using CaseIntake.TestResults;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>
/// E5: importer defects found while raising coverage. A suite downgraded to NoResults counts towards the run status,
/// a malformed run.json is reported without stopping the pass, and a raw server log survives a failed save.
/// </summary>
[Category("Story:E5-S2")]
public sealed class TestResultImporterRegressionTests : ClassFixture<TestResultImporterRegressionTests, PostgresDatabaseFixture>, IAsyncLifetime
{
    private static PostgresDatabaseFixture database => Shared;

    private const string WebSuite = "CaseIntake.Web.Tests";
    private const string NewSuite = "CaseIntake.Brand.New.Tests";

    [SetUp]

    public Task InitializeAsync() => database.ResetAsync();

    [TearDown]

    public Task DisposeAsync() => Task.CompletedTask;

    [Test]
    public async Task A_completed_suite_whose_result_file_is_missing_never_reads_as_a_completed_run()
    {
        using var h = new TestResultsTestHarness(database);
        h.NewRun("r-missing-only").SuiteReferencing(NewSuite, "Unit", "X.trx").Complete();
        h.NewRun("r-missing-one")
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")))
            .SuiteReferencing(NewSuite, "Unit", "X.trx")
            .Complete();
        var oversized = h.NewRun("r-oversized")
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "C")))
            .SuiteReferencing(NewSuite, "Unit", "Big.trx");
        using (var trx = File.Create(oversized.PathOf("Big.trx")))
        {
            trx.SetLength(256L * 1024 * 1024 + 1);   // sparse: only the length is checked
        }
        oversized.Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(3, summary.Imported);
        await using var db = database.CreateContext();
        var runs = await db.TestRuns.ToDictionaryAsync(r => r.RunKey);
        Assert.Equal((TestRunStatus.FailedToStart, 0), (runs["r-missing-only"].Status, runs["r-missing-only"].Total));
        Assert.Equal((TestRunStatus.Partial, 1), (runs["r-missing-one"].Status, runs["r-missing-one"].Total));
        Assert.Equal((TestRunStatus.Partial, 1), (runs["r-oversized"].Status, runs["r-oversized"].Total));
    }
    [TestCase("""{"runKey":"a-run","startedAtUtc":"2026-10-16T15:00:00Z","suites":[{"name":null,"status":"Completed","trx":"X.trx"}]}""")]
    [TestCase("""{"runKey":"a-run","startedAtUtc":"2026-10-16T15:00:00Z","suites":null}""")]
    [TestCase("""{"runKey":"a-run","startedAtUtc":"2026-10-16T15:00:00Z","suites":[null]}""")]
    [TestCase("""{"runKey":"a-run","startedAtUtc":"2026-10-16T15:00:00Z","suites":[],"serverLogs":null}""")]
    public async Task A_malformed_run_manifest_is_reported_and_later_folders_still_import(string runJson)
    {
        using var h = new TestResultsTestHarness(database);
        var bad = Path.Combine(h.RunsPath, "a-run");
        Directory.CreateDirectory(bad);
        await File.WriteAllTextAsync(Path.Combine(bad, "run.json"), runJson);
        h.NewRun("b-run").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B"))).Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(1, summary.Imported);
        Assert.Equal(["a-run: could not be imported (InvalidDataException)."], summary.Problems);
        await using var db = database.CreateContext();
        Assert.Equal("b-run", (await db.TestRuns.SingleAsync()).RunKey);
    }
    [TestCase("""{"fullName":"Ns.A.B","steps":null}""")]
    [TestCase("""{"fullName":"Ns.A.B","steps":[null]}""")]
    [TestCase("""{"fullName":"Ns.A.B","artifacts":null}""")]
    [TestCase("""{"fullName":"Ns.A.B","artifacts":[null]}""")]
    [TestCase("""{"fullName":"Ns.A.B","consoleErrors":null}""")]
    [TestCase("""{"fullName":"Ns.A.B","networkFailures":null}""")]
    public async Task An_evidence_manifest_with_null_lists_is_reported_and_its_run_still_imports(string evidenceJson)
    {
        using var h = new TestResultsTestHarness(database);
        var folder = h.NewRun("r-evidence-null").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")));
        Directory.CreateDirectory(folder.PathOf("evidence", "a"));
        await File.WriteAllTextAsync(folder.PathOf("evidence", "a", "evidence.json"), evidenceJson);
        folder.Complete();
        h.NewRun("r-later").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "C"))).Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(2, summary.Imported);
        Assert.Equal(["r-evidence-null: An evidence manifest could not be read."], summary.Problems);
        await using var db = database.CreateContext();
        var execution = await db.TestExecutions.Include(e => e.Steps).SingleAsync(e => e.Run.RunKey == "r-evidence-null");
        Assert.Equal((TestOutcome.Passed, null, 0), (execution.Outcome, execution.Browser, execution.Steps.Count));
    }
    [TestCase("""[null]""")]
    [TestCase("""[{"method":null,"categories":["Story:X"]}]""")]
    [TestCase("""[{"method":"Ns.A.B","categories":null}]""")]
    [TestCase("""[{"method":"Ns.A.B","categories":[null]}]""")]
    public async Task A_test_catalog_with_null_entries_is_reported_and_its_run_still_imports(string catalogJson)
    {
        using var h = new TestResultsTestHarness(database);
        var folder = h.NewRun("r-catalog-null").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")));
        Directory.CreateDirectory(folder.PathOf("catalog"));
        await File.WriteAllTextAsync(folder.PathOf("catalog", "Broken.json"), catalogJson);
        folder.Complete();
        h.NewRun("r-later").Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "C"))).Complete();

        var summary = await h.ImportAsync();

        Assert.Equal(2, summary.Imported);
        Assert.Equal(["r-catalog-null: A test catalog could not be read."], summary.Problems);
        await using var db = database.CreateContext();
        Assert.Null((await db.TestCases.SingleAsync(c => c.FullName == "Ns.A.B")).Story);
    }

    [Test]
    public async Task A_server_log_is_kept_until_its_run_is_saved_so_a_failed_save_can_be_retried()
    {
        var faults = new SaveFaults { FailBeforeCommit = true };
        using var h = new TestResultsTestHarness(database, null, faults);
        var folder = h.NewRun("r-save-log")
            .Suite(WebSuite, "Unit", Trx.Document(new TrxTest("Ns.A", "B")))
            .ServerLog("server.log", "Cookie: CaseIntake.Auth=secret\nplain line\n")
            .Complete();

        var failed = await h.ImportAsync();
        Assert.Equal(["r-save-log: not saved (DbUpdateException)."], failed.Problems);
        Assert.True(File.Exists(folder.PathOf("server.log")), "the raw log is kept while its run is not saved");

        faults.FailBeforeCommit = false;
        var retried = await h.ImportAsync();

        Assert.Equal(1, retried.Imported);
        Assert.Empty(retried.Problems);
        Assert.False(File.Exists(folder.PathOf("server.log")), "the raw log is removed once the run is saved");
        Assert.Equal("Cookie: [REDACTED]\nplain line\n", await File.ReadAllTextAsync(folder.PathOf("server.redacted.log")));
        await using var db = database.CreateContext();
        var log = await db.TestArtifacts.SingleAsync(a => a.Kind == TestArtifactKind.ServerLog);
        Assert.Equal("server.redacted.log", log.FileName);
    }
    [TestCase("sent .AspNetCore.Antiforgery.Xy-9_aB=CfDJ8abc; path=/", "sent .AspNetCore.Antiforgery.Xy-9_aB=[REDACTED]; path=/")]
    [TestCase("GET / 200 CaseIntake.AuthC1=CfDJ8one CaseIntake.AuthC2=CfDJ8two", "GET / 200 CaseIntake.AuthC1=[REDACTED] CaseIntake.AuthC2=[REDACTED]")]
    [TestCase("chunks: CaseIntake.Auth=chunks-2", "chunks: CaseIntake.Auth=[REDACTED]")]
    public void Antiforgery_cookies_with_base64url_names_and_chunked_auth_cookies_are_masked(string input, string expected)
    {
        Assert.Equal(expected, EvidenceRedactor.Redact(input));
    }
}
