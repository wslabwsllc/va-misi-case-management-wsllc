using CaseIntake.Cases;
using CaseIntake.Cases.Seeding;
using CaseIntake.Persistence.Seeding;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-C2-2: the Change 2 migration adds six nullable columns to a seeded database and leaves its rows unchanged.</summary>
public sealed class ServiceHistoryMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261021113919_Q1_Section508TestResults";
    private static readonly string[] NewColumns =
        ["ServiceHistoryStatus", "ServiceBranch", "ServiceStartDate", "ServiceEndDate", "CharacterOfService", "ServiceRecordVerifiedOn"];

    private readonly PostgresDatabaseFixture _database = new();

    [SetUp]

    public Task InitializeAsync() => _database.InitializeAsync();

    [TearDown]

    public Task DisposeAsync() => _database.DisposeAsync();

    [Test]
    public async Task Migration_over_the_seeded_cases_adds_nullable_columns_and_keeps_every_row()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_database.CreateContextFactory());
        services.AddCasesSeed();
        await using (var provider = services.BuildServiceProvider())
        {
            await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);
        }

        await using var db = _database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var before = await RowsAsync("row_to_json(c)::jsonb");

        await migrator.MigrateAsync();

        Assert.Equal(CaseSeedData.CaseCount, before.Count);
        Assert.Equal(before, await RowsAsync($"row_to_json(c)::jsonb - {Array(NewColumns)}"));
        Assert.All(await RowsAsync($"jsonb_strip_nulls(jsonb_build_object({string.Join(", ", NewColumns.Select(n => $"'{n}', c.\"{n}\""))}))"),
            row => Assert.Equal("{}", row));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "The model changed without a new migration.");
    }

    private static string Array(IEnumerable<string> names) => $"ARRAY[{string.Join(", ", names.Select(n => $"'{n}'"))}]";

    private async Task<List<string>> RowsAsync(string projection)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT ({projection})::text FROM \"Cases\" c ORDER BY \"Id\"", connection);
        List<string> rows = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }
        return rows;
    }
}
