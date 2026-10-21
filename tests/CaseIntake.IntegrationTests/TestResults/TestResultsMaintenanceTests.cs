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
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>
/// E5: evidence retention removes the files of old runs beyond the newest N while keeping every structured result, and
/// the background service imports new run folders on its own.
/// </summary>
[Trait("Category", "Story:E5-S7")]
public sealed class TestResultsMaintenanceTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset T = TestResultsTestHarness.StartTime;

    public Task InitializeAsync() => database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A run with a result file and one screenshot of a browser test.</summary>
    private static void Run(TestResultsTestHarness h, string key, DateTimeOffset started) =>
        h.NewRun(key, started)
            .Suite("CaseIntake.E2E", "EndToEnd", Trx.Document(started, new TrxTest("CaseIntake.E2E.Smoke", "Opens_the_dashboard")))
            .Evidence("smoke", new EvidenceManifest
            {
                FullName = "CaseIntake.E2E.Smoke.Opens_the_dashboard",
                Artifacts = [new EvidenceFile { Kind = "Screenshot", File = "final.png" }],
            }, ("final.png", [1, 2, 3]))
            .Complete();

    private static async Task<Dictionary<string, (int Purged, int Kept)>> ArtifactStateAsync(TestResultsTestHarness h) =>
        (await h.QueryAsync(db => db.TestArtifacts.Select(a => new { a.Run.RunKey, a.Purged }).ToListAsync()))
            .GroupBy(a => a.RunKey)
            .ToDictionary(g => g.Key, g => (g.Count(a => a.Purged), g.Count(a => !a.Purged)));

    [Fact]
    public async Task Evidence_of_runs_past_retention_is_deleted_while_results_and_the_newest_runs_stay()
    {
        using var h = new TestResultsTestHarness(database, o => { o.ArtifactRetention = TimeSpan.FromDays(30); o.KeepLatestRuns = 1; });
        Run(h, "r-60-days", T.AddDays(-60));
        Run(h, "r-45-days", T.AddDays(-45));
        Run(h, "r-10-days", T.AddDays(-10));
        await h.ImportAsync();
        var executionsBefore = await h.QueryAsync(db => db.TestExecutions.CountAsync());

        var purged = await h.Retention.PurgeAsync(CancellationToken.None);

        Assert.Equal(4, purged);
        var state = await ArtifactStateAsync(h);
        Assert.Equal((2, 0), state["r-60-days"]);
        Assert.Equal((2, 0), state["r-45-days"]);
        Assert.Equal((0, 2), state["r-10-days"]);
        var purgedRows = await h.QueryAsync(db => db.TestArtifacts.Where(a => a.Purged).ToListAsync());
        Assert.All(purgedRows, a =>
        {
            Assert.Equal(T, a.PurgedAtUtc);
            Assert.False(File.Exists(Path.Combine(h.Root, a.RelativePath)), $"{a.RelativePath} was not deleted");
        });
        var kept = await h.QueryAsync(db => db.TestArtifacts.Where(a => !a.Purged).ToListAsync());
        Assert.All(kept, a => Assert.True(File.Exists(Path.Combine(h.Root, a.RelativePath))));
        Assert.Equal(executionsBefore, await h.QueryAsync(db => db.TestExecutions.CountAsync()));

        var audit = await h.QueryAsync(db => db.TestResultAuditEntries.SingleAsync(a => a.Action == "ArtifactsPurged"));
        Assert.Equal(("system (retention)", (string?)null, "4 evidence files of 2 runs older than 30 days"), (audit.Actor, audit.ActorId, audit.Details));
        var log = Assert.Single(h.RetentionLogs.Collector.GetSnapshot(), r => r.Id.Id == 5205);
        Assert.Equal(("4", "2"), (log.GetStructuredStateValue("Files"), log.GetStructuredStateValue("Runs")));

        var shot = purgedRows.First(a => a.Kind == TestArtifactKind.Screenshot);
        var opened = await h.Service.OpenArtifactAsync(shot.Id, false, CancellationToken.None);
        Assert.Equal("TestArtifactPurged", TestResultsTestHarness.Fail(opened, ErrorKind.NotFound).Code);

        Assert.Equal(0, await h.Retention.PurgeAsync(CancellationToken.None));   // nothing left to purge, and no new audit entry
        Assert.Equal(1, await h.QueryAsync(db => db.TestResultAuditEntries.CountAsync(a => a.Action == "ArtifactsPurged")));
    }

    [Fact]
    public async Task The_newest_runs_keep_their_evidence_however_old_they_are()
    {
        using var h = new TestResultsTestHarness(database, o => { o.ArtifactRetention = TimeSpan.FromDays(30); o.KeepLatestRuns = 2; });
        Run(h, "r-400-days", T.AddDays(-400));
        Run(h, "r-300-days", T.AddDays(-300));
        Run(h, "r-200-days", T.AddDays(-200));
        await h.ImportAsync();

        Assert.Equal(2, await h.Retention.PurgeAsync(CancellationToken.None));

        var state = await ArtifactStateAsync(h);
        Assert.Equal((2, 0), state["r-400-days"]);
        Assert.Equal((0, 2), state["r-300-days"]);
        Assert.Equal((0, 2), state["r-200-days"]);
    }

    [Fact]
    public async Task Zero_retention_keeps_everything()
    {
        using var h = new TestResultsTestHarness(database, o => { o.ArtifactRetention = TimeSpan.Zero; o.KeepLatestRuns = 1; });
        Run(h, "r-old", T.AddDays(-1000));
        Run(h, "r-new", T);
        await h.ImportAsync();

        Assert.Equal(0, await h.Retention.PurgeAsync(CancellationToken.None));
        Assert.Equal(0, await h.QueryAsync(db => db.TestArtifacts.CountAsync(a => a.Purged)));
    }

    [Fact]
    public async Task Evidence_already_gone_is_marked_purged_and_files_that_cannot_be_deleted_are_retried_next_pass()
    {
        if (OperatingSystem.IsWindows())
        {
            return;   // Unix file modes are used to make a file undeletable; the tests service runs on Linux.
        }
        using var h = new TestResultsTestHarness(database, o => { o.ArtifactRetention = TimeSpan.FromDays(30); o.KeepLatestRuns = 1; });
        Run(h, "r-old", T.AddDays(-60));
        Run(h, "r-new", T);
        await h.ImportAsync();
        var trx = Path.Combine(h.RunsPath, "r-old", "CaseIntake.E2E.trx");
        File.Delete(trx);
        var evidenceFolder = Path.Combine(h.RunsPath, "r-old", "evidence", "smoke");
        var mode = File.GetUnixFileMode(evidenceFolder);
        File.SetUnixFileMode(evidenceFolder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        int first;
        try
        {
            first = await h.Retention.PurgeAsync(CancellationToken.None);
        }
        finally
        {
            File.SetUnixFileMode(evidenceFolder, mode);
        }

        Assert.Equal(1, first);   // the missing result file
        Assert.Equal((1, 1), (await ArtifactStateAsync(h))["r-old"]);
        Assert.True(File.Exists(Path.Combine(evidenceFolder, "final.png")));
        var audit = await h.QueryAsync(db => db.TestResultAuditEntries.SingleAsync(a => a.Action == "ArtifactsPurged"));
        Assert.Equal("1 evidence files of 1 runs older than 30 days", audit.Details);

        Assert.Equal(1, await h.Retention.PurgeAsync(CancellationToken.None));
        Assert.Equal((2, 0), (await ArtifactStateAsync(h))["r-old"]);
        Assert.False(File.Exists(Path.Combine(evidenceFolder, "final.png")));
    }

    [Fact]
    public async Task The_module_registers_the_service_factory_and_validates_options_at_startup()
    {
        var root = Path.Combine(Path.GetTempPath(), "caseintake-testresults-" + Guid.NewGuid().ToString("N"));
        await using var provider = Provider(database.CreateContextFactory(), new() { ["TestResults:RootPath"] = root, ["TestResults:ScanInterval"] = "00:00:00" });

        Assert.Equal(Path.GetFullPath(root), provider.GetRequiredService<IOptions<TestResultsOptions>>().Value.RootPath);
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.IsType<TestResultsService>(scope.ServiceProvider.GetRequiredService<ITestResultsService>());
            var factory = scope.ServiceProvider.GetRequiredService<ITestResultsServiceFactory>();
            var viewer = factory.Create(new FakeCurrentUser { Actor = TestResultsActors.ViewOnly });
            var nobody = factory.Create(new FakeCurrentUser { Actor = TestResultsActors.NoPermissions });
            Assert.Empty(TestResultsTestHarness.Ok(await viewer.ListModulesAsync(CancellationToken.None)));
            TestResultsTestHarness.Fail(await nobody.ListModulesAsync(CancellationToken.None), ErrorKind.Forbidden);
            Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
        }
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddTestResultsModule(null!));

        await using var invalid = Provider(database.CreateContextFactory(), new() { ["TestResults:KeepLatestRuns"] = "0" });
        var error = Assert.Throws<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<TestResultsOptions>>().Value);
        Assert.Contains("TestResults:KeepLatestRuns must be at least 1.", error.Failures);
    }

    [Fact]
    public async Task The_background_import_does_nothing_when_its_interval_is_zero()
    {
        await using var provider = Provider(database.CreateContextFactory(), new() { ["TestResults:ScanInterval"] = "00:00:00" });
        var service = provider.GetServices<IHostedService>().OfType<BackgroundService>().Single();

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));   // returns at once instead of waiting for the first pass

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
        await using var db = database.CreateContext();
        Assert.False(await db.TestRuns.AnyAsync());
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_background_import_logs_a_failed_pass_and_imports_new_runs_on_the_next_one()
    {
        using var h = new TestResultsTestHarness(database);
        Run(h, "r-background", T);
        var factory = new FailingOnceFactory(database.CreateContextFactory());
        await using var provider = Provider(factory, new() { ["TestResults:RootPath"] = h.Root, ["TestResults:ScanInterval"] = "00:00:00.200" });
        var service = provider.GetServices<IHostedService>().OfType<BackgroundService>().Single();

        await service.StartAsync(CancellationToken.None);
        var imported = false;
        for (var i = 0; i < 150 && !imported; i++)
        {
            await Task.Delay(100);
            imported = await h.QueryAsync(db => db.TestRuns.AnyAsync(r => r.RunKey == "r-background"));
        }
        await service.StopAsync(CancellationToken.None);

        Assert.True(imported, "the background service did not import the run");
        var run = await h.QueryAsync(db => db.TestRuns.SingleAsync());
        Assert.Equal("system (automatic import)", run.ImportedBy);
        Assert.Null((await h.QueryAsync(db => db.TestResultAuditEntries.SingleAsync())).ActorId);
        var failed = Assert.Single(provider.GetFakeLogCollector().GetSnapshot(), r => r.Id.Id == 5207);
        Assert.Equal(nameof(InvalidOperationException), failed.GetStructuredStateValue("ExceptionType"));
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    private static ServiceProvider Provider(IDbContextFactory<AppDbContext> factory, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFakeLogging());
        services.AddSingleton(configuration);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(factory);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(T));
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser { Actor = TestResultsActors.QualityAnalyst });
        services.AddTestResultsModule(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>The first context request fails (a database outage during one background pass); later ones work.</summary>
    private sealed class FailingOnceFactory(IDbContextFactory<AppDbContext> inner) : IDbContextFactory<AppDbContext>
    {
        private int _calls;

        public AppDbContext CreateDbContext() =>
            Interlocked.Increment(ref _calls) == 1 ? throw new InvalidOperationException("Simulated outage.") : inner.CreateDbContext();
    }
}
