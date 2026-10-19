using CaseIntake.Attachments.Storage;
using CaseIntake.Attachments;
using CaseIntake.Persistence;
using CaseIntake.TestKit;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Attachments;

/// <summary>E4-S5/D9: startup cleanup removes only stale, unreferenced files and never committed content.</summary>
public class AttachmentReconcilerTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    internal static AttachmentStorageReconciler Reconciler(AttachmentTestHarness h, FakeLogger<AttachmentStorageReconciler>? logs = null) =>
        new(h.Cases.Factory, h.Store, h.Options, TimeProvider.System, logs ?? new FakeLogger<AttachmentStorageReconciler>());

    internal static void Age(string path, TimeSpan age) =>
        File.SetLastWriteTimeUtc(path, TimeProvider.System.GetUtcNow().UtcDateTime - age);

    private static string WriteFile(params string[] segments)
    {
        var path = Path.Combine(segments);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [9]);
        return path;
    }

    [Fact]
    public async Task Stale_temporary_and_unreferenced_files_are_removed_while_committed_and_recent_files_stay()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var committed = await h.UploadAsync(c.Id, [1, 2, 3]);
        var committedPath = Assert.Single(h.FilesUnderRoot());
        Age(committedPath, TimeSpan.FromDays(30));

        var staleTemp = WriteFile(h.Root, "tmp", "old.part");
        var recentTemp = WriteFile(h.Root, "tmp", "new.part");
        var staleKey = Guid.NewGuid().ToString("N");
        var staleOrphan = WriteFile(h.Root, "blobs", FileSystemAttachmentStore.ShardOf(staleKey), staleKey);
        var recentKey = Guid.NewGuid().ToString("N");
        var recentOrphan = WriteFile(h.Root, "blobs", FileSystemAttachmentStore.ShardOf(recentKey), recentKey);
        Age(staleTemp, TimeSpan.FromHours(2));
        Age(staleOrphan, TimeSpan.FromHours(2));
        var logs = new FakeLogger<AttachmentStorageReconciler>();

        await Reconciler(h, logs).RunAsync(CancellationToken.None);

        Assert.True(File.Exists(committedPath), "committed content was deleted");
        Assert.True(File.Exists(recentTemp), "an upload that may still be in progress was deleted");
        Assert.True(File.Exists(recentOrphan), "content younger than the grace period was deleted");
        Assert.False(File.Exists(staleTemp));
        Assert.False(File.Exists(staleOrphan));
        var summary = Assert.Single(logs.Collector.GetSnapshot(), r => r.Id.Id == 4110);
        Assert.Equal("1", summary.GetStructuredStateValue("TempDeleted"));
        Assert.Equal("1", summary.GetStructuredStateValue("OrphansDeleted"));
        Assert.NotEqual(Guid.Empty, committed.Id);
    }

    [Fact]
    public async Task The_migrator_entry_point_reconciles_the_configured_root_and_can_run_again_safely()
    {
        var root = Path.Combine(Path.GetTempPath(), "caseintake-reconcile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stale = WriteFile(root, "tmp", "stale.part");
            Age(stale, TimeSpan.FromHours(2));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CaseIntake"] = database.ConnectionString,
                ["Attachments:RootPath"] = root,
            }).Build();
            var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddFakeLogging();
            services.AddPersistence(configuration).AddAttachmentsMaintenance();   // what MigrationService/Program.cs registers
            await using var provider = services.BuildServiceProvider();

            await provider.ReconcileAttachmentStorageAsync(CancellationToken.None);
            await provider.ReconcileAttachmentStorageAsync(CancellationToken.None);

            Assert.False(File.Exists(stale));
            var runs = provider.GetFakeLogCollector().GetSnapshot().Where(r => r.Id.Id == 4110).ToList();
            Assert.Equal(2, runs.Count);
            Assert.Equal("1", runs[0].GetStructuredStateValue("TempDeleted"));
            Assert.Equal("0", runs[1].GetStructuredStateValue("TempDeleted"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_file_that_cannot_be_deleted_is_counted_as_a_failure_and_the_run_still_completes()
    {
        using var h = new AttachmentTestHarness(database);
        var stale = WriteFile(h.Root, "tmp", "stuck.part");
        Age(stale, TimeSpan.FromHours(2));
        h.Store.FailDeleteFile = true;
        var logs = new FakeLogger<AttachmentStorageReconciler>();

        await Reconciler(h, logs).RunAsync(CancellationToken.None);

        var summary = Assert.Single(logs.Collector.GetSnapshot(), r => r.Id.Id == 4110);
        Assert.Equal("0", summary.GetStructuredStateValue("TempDeleted"));
        Assert.Equal("1", summary.GetStructuredStateValue("Failures"));
        Assert.True(File.Exists(stale));
    }

    [Fact]
    public async Task Attachments_whose_content_is_missing_are_counted_not_deleted()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var before = await h.CountAttachmentsAsync();
        await h.UploadAsync(c.Id, [1, 2, 3]);
        File.Delete(Assert.Single(h.FilesUnderRoot()));
        var logs = new FakeLogger<AttachmentStorageReconciler>();

        await Reconciler(h, logs).RunAsync(CancellationToken.None);

        Assert.Equal(before + 1, await h.CountAttachmentsAsync());
        var summary = Assert.Single(logs.Collector.GetSnapshot(), r => r.Id.Id == 4110);
        Assert.True(int.Parse(summary.GetStructuredStateValue("MissingContent")!, System.Globalization.CultureInfo.InvariantCulture) >= 1);
    }

    [Fact]
    public async Task A_missing_root_is_not_an_error()
    {
        using var h = new AttachmentTestHarness(database);
        var logs = new FakeLogger<AttachmentStorageReconciler>();

        await Reconciler(h, logs).RunAsync(CancellationToken.None);

        Assert.Single(logs.Collector.GetSnapshot(), r => r.Id.Id == 4110);
    }

    [Fact]
    public async Task A_failure_is_logged_and_does_not_stop_startup()
    {
        using var h = new AttachmentTestHarness(database);
        h.QueryFaults.Fail = true;
        var logs = new FakeLogger<AttachmentStorageReconciler>();

        await Reconciler(h, logs).RunAsync(CancellationToken.None);

        Assert.Single(logs.Collector.GetSnapshot(), r => r.Id.Id == 4111);
    }
}
