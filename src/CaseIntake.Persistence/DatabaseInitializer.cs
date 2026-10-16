using CaseIntake.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Persistence;

public sealed record DatabaseInitializerOptions
{
    /// <summary>Total time to keep retrying while the database is not reachable.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(8);
}

/// <summary>Applies migrations (with backoff while the database is starting) and then runs the seed contributors.</summary>
public sealed partial class DatabaseInitializer(
    IDbContextFactory<AppDbContext> contextFactory,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILogger<DatabaseInitializer> logger)
{
    public async Task MigrateAsync(DatabaseInitializerOptions? options, CancellationToken ct)
    {
        options ??= new DatabaseInitializerOptions();
        var started = timeProvider.GetTimestamp();
        var delay = options.InitialDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await contextFactory.CreateDbContextAsync(ct);
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
                LogMigrating(pending);
                await db.Database.MigrateAsync(ct);
                LogMigrated();
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
            {
                var elapsed = timeProvider.GetElapsedTime(started);
                if (elapsed + delay > options.MaxWait)
                {
                    LogDatabaseUnavailable(ex, (int)elapsed.TotalSeconds, attempt);
                    throw;
                }
                LogWaitingForDatabase(attempt, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, timeProvider, ct);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxDelay.Ticks));
            }
        }
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        var runner = ActivatorUtilities.CreateInstance<SeedRunner>(services);
        await runner.RunAsync(ct);
    }

    /// <summary>
    /// Connection-level failures while the database container is still starting (not schema or SQL errors). The
    /// provider's own retrying execution strategy wraps the last failure in <see cref="RetryLimitExceededException"/>,
    /// so that wrapper is unwrapped too; otherwise this outer backoff would never run in production.
    /// </summary>
    internal static bool IsTransient(Exception ex) => ex switch
    {
        Npgsql.NpgsqlException npgsql => npgsql.IsTransient || npgsql.InnerException is System.Net.Sockets.SocketException or IOException,
        RetryLimitExceededException { InnerException: { } inner } => IsTransient(inner),
        InvalidOperationException { InnerException: { } inner } => IsTransient(inner),
        System.Net.Sockets.SocketException or IOException or TimeoutException => true,
        _ => false,
    };

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Applying database migrations ({PendingCount} pending)")]
    private partial void LogMigrating(int pendingCount);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Database migrations are up to date")]
    private partial void LogMigrated();

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Database not ready (attempt {Attempt}); retrying in {DelayMs} ms")]
    private partial void LogWaitingForDatabase(int attempt, int delayMs);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Critical, Message = "Database was not reachable after {ElapsedSeconds} s and {Attempts} attempts; giving up. Check that the db service is healthy and ConnectionStrings__CaseIntake is correct")]
    private partial void LogDatabaseUnavailable(Exception exception, int elapsedSeconds, int attempts);
}
