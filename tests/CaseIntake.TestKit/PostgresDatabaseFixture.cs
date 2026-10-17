using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace CaseIntake.TestKit;

/// <summary>
/// Gives each fixture instance its own freshly created database, so fixtures that run concurrently never share or
/// reset each other's data, and never touch the demo database. The server is the <c>db</c> service when
/// <c>TESTKIT_POSTGRES_ADMIN</c> is set (Compose <c>tests</c> service); otherwise a disposable Testcontainers
/// PostgreSQL 17 is started once per test process. The database is dropped on disposal.
/// </summary>
public class PostgresDatabaseFixture : IAsyncLifetime
{
    public const string AdminConnectionEnvironmentVariable = "TESTKIT_POSTGRES_ADMIN";
    private static readonly Lazy<Task<string>> AdminConnection = new(StartServerAsync);

    // CREATE/DROP DATABASE contend for server-wide locks and can each take seconds on a busy server. Running them one at
    // a time per test process, with a generous timeout, stops parallel fixtures from timing out (Npgsql's default is 30 s).
    private static readonly SemaphoreSlim AdminOperations = new(1, 1);
    private const int AdminCommandTimeoutSeconds = 300;

    private string _adminConnectionString = null!;
    private Respawner? _respawner;

    public string DatabaseName { get; } = $"caseintake_test_{Guid.NewGuid():N}";

    /// <summary>Connection string of this fixture's private database (UTC session).</summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>When true (default) the EF migrations are applied after creating the database.</summary>
    protected virtual bool ApplyMigrations => true;

    public async Task InitializeAsync()
    {
        _adminConnectionString = await AdminConnection.Value;
        await RunAdminCommandAsync($"CREATE DATABASE \"{DatabaseName}\"");

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = DatabaseName,
            Timezone = "UTC",
            Pooling = false,
        }.ConnectionString;

        if (ApplyMigrations)
        {
            await using var db = CreateContext();
            await db.Database.MigrateAsync();
        }
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public IDbContextFactory<AppDbContext> CreateContextFactory() => new SimpleFactory(this);

    /// <summary>Deletes all rows (except the EF migrations history) so a class can start from a clean database.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        if (_adminConnectionString is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await RunAdminCommandAsync($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }

    private async Task RunAdminCommandAsync(string sql)
    {
        await AdminOperations.WaitAsync();
        try
        {
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand(sql, admin) { CommandTimeout = AdminCommandTimeoutSeconds };
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            AdminOperations.Release();
        }
    }

    private static async Task<string> StartServerAsync()
    {
        var configured = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var container = new PostgreSqlBuilder("postgres:17").Build();
        await container.StartAsync();   // reaped by Testcontainers' Ryuk when the process exits
        return container.GetConnectionString();
    }

    private sealed class SimpleFactory(PostgresDatabaseFixture fixture) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => fixture.CreateContext();
    }
}

/// <summary>Fixture variant for tests that apply migrations themselves.</summary>
public sealed class EmptyPostgresDatabaseFixture : PostgresDatabaseFixture
{
    protected override bool ApplyMigrations => false;
}
