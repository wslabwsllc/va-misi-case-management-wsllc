using CaseIntake.Domain.TestResults;
using CaseIntake.Persistence;
using CaseIntake.TestResults.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.TestResults;

/// <summary>
/// Evidence retention: deletes the evidence files of runs that are older than <see cref="TestResultsOptions.ArtifactRetention"/>
/// and not among the newest <see cref="TestResultsOptions.KeepLatestRuns"/>, and marks their metadata as purged. Structured
/// results (runs, executions, steps) are never deleted, so history and trends stay complete.
/// </summary>
internal sealed class TestArtifactRetention(
    IDbContextFactory<AppDbContext> contextFactory,
    IOptions<TestResultsOptions> options,
    TimeProvider time,
    ILogger<TestArtifactRetention> logger)
{
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (settings.ArtifactRetention <= TimeSpan.Zero)
        {
            return 0;
        }
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var cutoff = time.GetUtcNow() - settings.ArtifactRetention;
        var keep = await db.TestRuns.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id)
            .Take(settings.KeepLatestRuns).Select(r => r.Id).ToListAsync(ct);
        var expired = await db.TestArtifacts
            .Where(a => !a.Purged && !keep.Contains(a.RunId) && a.Run.StartedAtUtc < cutoff)
            .Take(1000).ToListAsync(ct);
        if (expired.Count == 0)
        {
            return 0;
        }
        foreach (var artifact in expired)
        {
            if (TestResultImporter.SafeChild(settings.RootPath, artifact.RelativePath) is { } path && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;   // retried on the next pass
                }
            }
            artifact.Purged = true;
            artifact.PurgedAtUtc = time.GetUtcNow();
        }
        var runs = expired.Select(a => a.RunId).Distinct().Count();
        db.TestResultAuditEntries.Add(new TestResultAuditEntry
        {
            OccurredAtUtc = time.GetUtcNow(), Actor = "system (retention)", Action = "ArtifactsPurged",
            Details = $"{expired.Count(a => a.Purged)} evidence files of {runs} runs older than {settings.ArtifactRetention.TotalDays:0} days",
        });
        await db.SaveChangesAsync(ct);
        TestResultsLog.ArtifactsPurged(logger, expired.Count(a => a.Purged), runs);
        return expired.Count(a => a.Purged);
    }
}

/// <summary>
/// Imports newly written run folders and applies retention in the background (default: every minute). This never runs
/// tests; tests are only started by the documented command. Disabled when <see cref="TestResultsOptions.ScanInterval"/>
/// is zero.
/// </summary>
internal sealed class TestResultsBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<TestResultsOptions> options,
    ILogger<TestResultsBackgroundService> logger) : BackgroundService
{
    public const string SystemActor = "system (automatic import)";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.ScanInterval;
        if (interval <= TimeSpan.Zero)
        {
            return;
        }
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<TestResultImporter>().ImportPendingAsync(SystemActor, null, stoppingToken);
                    await scope.ServiceProvider.GetRequiredService<TestArtifactRetention>().PurgeAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    TestResultsLog.BackgroundFailed(logger, ex.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
