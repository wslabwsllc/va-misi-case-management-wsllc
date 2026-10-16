using CaseIntake.Domain.Cases;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-S1: the database itself enforces ID, title, description and status rules.</summary>
public class CaseSchemaTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    [Fact]
    public async Task Ids_are_positive_system_generated_bigints()
    {
        var harness = new CaseTestHarness(database);

        var first = await harness.CreateAsync();
        var second = await harness.CreateAsync();

        Assert.True(first.Id > 0);
        Assert.True(second.Id > first.Id);
        Assert.Equal("bigint", await ScalarAsync<string>(
            "SELECT data_type FROM information_schema.columns WHERE table_name = 'Cases' AND column_name = 'Id'"));
        Assert.Equal("ALWAYS", await ScalarAsync<string>(
            "SELECT identity_generation FROM information_schema.columns WHERE table_name = 'Cases' AND column_name = 'Id'"));
    }

    [Fact]
    public async Task Changing_a_saved_id_throws()
    {
        var created = await new CaseTestHarness(database).CreateAsync();
        await using var db = database.CreateContext();
        var entity = await db.Cases.SingleAsync(c => c.Id == created.Id);

        // No public setter, and EF refuses to mark the saved key as modified.
        Assert.False(typeof(Case).GetProperty(nameof(Case.Id))!.SetMethod!.IsPublic);
        Assert.Throws<InvalidOperationException>(() => db.Entry(entity).Property(c => c.Id).CurrentValue = created.Id + 1000);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw,
            db.Model.FindEntityType(typeof(Case))!.FindProperty(nameof(Case.Id))!.GetAfterSaveBehavior());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal(created.Title, (await new CaseTestHarness(database).LoadAsync(created.Id)).Title);
    }

    [Fact]
    public async Task Id_cannot_be_supplied_by_an_insert()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO \"Cases\" (\"Id\", \"Title\", \"Status\", \"CreatedAtUtc\", \"CreatedBy\", \"UpdatedAtUtc\", \"UpdatedBy\") " +
            "VALUES (-5, 'Valid title', 0, now(), 'x', now(), 'x')"));

        Assert.Equal("428C9", ex.SqlState);   // generated_always: identity ALWAYS refuses supplied values
    }

    [Theory]
    [InlineData("abcd", null, 0, PostgresErrorCodes.CheckViolation)]                 // 4-char title
    [InlineData("T201", null, 0, PostgresErrorCodes.StringDataRightTruncation)]      // 201-char title
    [InlineData("Valid title", "D4001", 0, PostgresErrorCodes.StringDataRightTruncation)]
    [InlineData("Valid title", null, 5, PostgresErrorCodes.CheckViolation)]          // status outside 0-4
    [InlineData("Valid title", null, -1, PostgresErrorCodes.CheckViolation)]
    public async Task Database_constraints_reject_invalid_rows(string title, string? description, int status, string sqlState)
    {
        title = title == "T201" ? new string('t', 201) : title;
        description = description == "D4001" ? new string('d', 4001) : description;

        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(title, description, status));

        Assert.Equal(sqlState, ex.SqlState);
    }

    [Fact]
    public async Task Boundary_values_are_accepted_by_the_database()
    {
        await InsertAsync(new string('t', 5), null, 0);
        await InsertAsync(new string('t', 200), new string('d', 4000), 4);
    }

    [Fact]
    public async Task Database_counts_supplementary_characters_like_the_service()
    {
        const string supplementary = "\U0001D538";
        var harness = new CaseTestHarness(database);

        var five = await harness.CreateAsync(string.Concat(Enumerable.Repeat(supplementary, 5)));
        var max = await harness.CreateAsync(string.Concat(Enumerable.Repeat(supplementary, 200)), string.Concat(Enumerable.Repeat(supplementary, 4000)));

        Assert.Equal(5, await ScalarAsync<int>($"SELECT char_length(\"Title\") FROM \"Cases\" WHERE \"Id\" = {five.Id}"));
        Assert.Equal(200, await ScalarAsync<int>($"SELECT char_length(\"Title\") FROM \"Cases\" WHERE \"Id\" = {max.Id}"));
    }

    [Fact]
    public async Task Columns_have_the_specified_types()
    {
        var columns = new Dictionary<string, string>();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name, data_type, coalesce(character_maximum_length::text, ''), is_nullable FROM information_schema.columns WHERE table_name = 'Cases'", connection);
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns[reader.GetString(0)] = $"{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}";
            }
        }

        Assert.Equal("character varying|200|NO", columns["Title"]);
        Assert.Equal("character varying|4000|YES", columns["Description"]);
        Assert.Equal("integer||NO", columns["Status"]);
        Assert.Equal("timestamp with time zone||NO", columns["CreatedAtUtc"]);
        Assert.Equal("timestamp with time zone||NO", columns["UpdatedAtUtc"]);
        Assert.Equal("character(4)", (await ScalarAsync<string>("SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = '\"Cases\"'::regclass AND attname = 'VeteranSsnLast4'")));
        Assert.DoesNotContain(columns.Keys, c => c.Contains("Ssn", StringComparison.Ordinal) && c != "VeteranSsnLast4");

        using var db = database.CreateContext();
        var version = db.Model.FindEntityType(typeof(Case))!.FindProperty(nameof(Case.Version))!;
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("xmin", version.GetColumnName());
    }

    private Task InsertAsync(string title, string? description, int status) => ExecuteAsync(
        "INSERT INTO \"Cases\" (\"Title\", \"Description\", \"Status\", \"CreatedAtUtc\", \"CreatedBy\", \"UpdatedAtUtc\", \"UpdatedBy\") " +
        "VALUES (@title, @description, @status, now(), 'x', now(), 'x')",
        new NpgsqlParameter("title", title), new NpgsqlParameter("description", (object?)description ?? DBNull.Value), new NpgsqlParameter("status", status));

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddRange(parameters);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
