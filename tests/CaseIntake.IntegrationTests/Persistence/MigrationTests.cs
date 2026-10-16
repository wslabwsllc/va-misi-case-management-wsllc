using CaseIntake.Persistence;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace CaseIntake.IntegrationTests.Persistence;

/// <summary>
/// Each test gets its own empty database (xUnit creates a class instance per test), so the "nothing applied yet"
/// precondition never depends on test order.
/// </summary>
public sealed class MigrationTests : IAsyncLifetime
{
    private readonly EmptyPostgresDatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Migrating_an_empty_database_applies_everything_and_leaves_no_pending_model_changes()
    {
        await using var db = _database.CreateContext();
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "The model changed without a new migration.");
    }

    [Fact]
    public async Task Migrating_twice_is_idempotent()
    {
        await using var db = _database.CreateContext();
        await db.Database.MigrateAsync();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.NotEmpty(applied);

        await db.Database.MigrateAsync();

        Assert.Equal(applied, (await db.Database.GetAppliedMigrationsAsync()).ToList());
    }

    [Fact]
    public async Task Initializer_migrates_through_the_production_registration()
    {
        await using var provider = BuildProductionServices(_database.ConnectionString, new FakeLogger<DatabaseInitializer>());

        await provider.GetRequiredService<DatabaseInitializer>().MigrateAsync(options: null, CancellationToken.None);

        await using var db = _database.CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    /// <summary>Uses the production registration (EF retrying execution strategy enabled), not a bare context.</summary>
    [Fact]
    public async Task Initializer_retries_with_backoff_then_gives_up_with_a_clear_error()
    {
        var unreachable = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = 1, Database = "x", Username = "x", Password = "x", Timeout = 1, Pooling = false,
        }.ConnectionString;
        var logs = new FakeLogger<DatabaseInitializer>();
        await using var provider = BuildProductionServices(unreachable, logs);
        var initializer = provider.GetRequiredService<DatabaseInitializer>();

        await Assert.ThrowsAnyAsync<Exception>(() => initializer.MigrateAsync(
            new DatabaseInitializerOptions { MaxWait = TimeSpan.FromSeconds(2), InitialDelay = TimeSpan.FromMilliseconds(200), MaxDelay = TimeSpan.FromMilliseconds(400) },
            CancellationToken.None));

        Assert.Contains(logs.Collector.GetSnapshot(), r => r.Id.Id == 1003);   // retried with backoff
        var final = Assert.Single(logs.Collector.GetSnapshot(), r => r.Id.Id == 1004);
        Assert.Contains("giving up", final.Message);
    }

    /// <summary>
    /// <c>MigrateAsync</c> runs inside EF's retrying execution strategy, which wraps the last connection failure in
    /// <see cref="RetryLimitExceededException"/>. The outer 60 s backoff must still see that as "database not ready".
    /// </summary>
    [Fact]
    public void Connection_failures_wrapped_by_the_provider_retry_strategy_are_transient()
    {
        var connectionFailure = new NpgsqlException("connect failed", new System.Net.Sockets.SocketException());

        Assert.True(DatabaseInitializer.IsTransient(connectionFailure));
        Assert.True(DatabaseInitializer.IsTransient(new RetryLimitExceededException("retries exhausted", connectionFailure)));
        Assert.False(DatabaseInitializer.IsTransient(new RetryLimitExceededException("retries exhausted", new FormatException())));
    }

    private static ServiceProvider BuildProductionServices(string connectionString, FakeLogger<DatabaseInitializer> logs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}"] = connectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(configuration);
        services.AddSingleton<ILogger<DatabaseInitializer>>(logs);
        return services.BuildServiceProvider();
    }
}
