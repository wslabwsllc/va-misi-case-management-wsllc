using CaseIntake.TestKit;
using Npgsql;
using NpgsqlTypes;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-C2-2: the database repeats the service-history rules, so no inconsistent row can be stored.</summary>
[Category("Story:E2-C2-2")]
[Category("Req:R-CR2-2")]
public class ServiceHistorySchemaTests : ClassFixture<ServiceHistorySchemaTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    private const int Found = 0;
    private const int NotFound = 1;
    private const int Unavailable = 2;
    private static readonly DateOnly Start = new(2001, 2, 15);
    private static readonly DateOnly End = new(2005, 2, 14);

    [Test]
    public async Task Status_outside_the_enum_is_rejected() =>
        await AssertRejectedAsync("CK_Case_ServiceHistory_Range", new Row { Status = 3 });

    [Test]
    public async Task Status_without_a_veteran_is_rejected() =>
        await AssertRejectedAsync("CK_Case_ServiceHistory_RequiresVeteran", new Row { Status = NotFound, WithVeteran = false });

    [Test]
    public async Task Found_without_branch_start_or_character_is_rejected()
    {
        await AssertRejectedAsync("CK_Case_ServiceHistory_Complete", Row.FoundRow with { Branch = null });
        await AssertRejectedAsync("CK_Case_ServiceHistory_Complete", Row.FoundRow with { StartDate = null });
        await AssertRejectedAsync("CK_Case_ServiceHistory_Complete", Row.FoundRow with { Character = null });
    }

    [TestCase(NotFound)]
    [TestCase(Unavailable)]
    [TestCase(null)]
    public async Task History_values_without_Found_are_rejected(int? status)
    {
        await AssertRejectedAsync("CK_Case_ServiceHistory_Complete", Row.FoundRow with { Status = status });
        await AssertRejectedAsync("CK_Case_ServiceHistory_Complete", new Row { Status = status, VerifiedOn = End });
        await AssertRejectedAsync("CK_Case_ServiceHistory_Complete", new Row { Status = status, EndDate = End });
    }

    [Test]
    public async Task End_before_start_is_rejected() =>
        await AssertRejectedAsync("CK_Case_ServiceHistory_Dates", Row.FoundRow with { EndDate = Start.AddDays(-1) });

    [Test]
    public async Task Consistent_rows_are_accepted()
    {
        await InsertAsync(Row.FoundRow);
        await InsertAsync(Row.FoundRow with { EndDate = null, VerifiedOn = null });
        await InsertAsync(Row.FoundRow with { EndDate = Start });
        await InsertAsync(Row.FoundRow with { Branch = new string('b', 50), Character = new string('c', 100) });
        await InsertAsync(new Row { Status = NotFound });
        await InsertAsync(new Row { Status = Unavailable });
        await InsertAsync(new Row());
        await InsertAsync(new Row { WithVeteran = false });
    }

    [Test]
    public async Task Text_columns_are_capped_at_the_field_limits()
    {
        var branch = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(Row.FoundRow with { Branch = new string('b', 51) }));
        var character = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(Row.FoundRow with { Character = new string('c', 101) }));

        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, branch.SqlState);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, character.SqlState);
    }

    [Test]
    public async Task New_columns_are_nullable_with_the_specified_types()
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

        Assert.Equal("integer||YES", columns["ServiceHistoryStatus"]);
        Assert.Equal("character varying|50|YES", columns["ServiceBranch"]);
        Assert.Equal("date||YES", columns["ServiceStartDate"]);
        Assert.Equal("date||YES", columns["ServiceEndDate"]);
        Assert.Equal("character varying|100|YES", columns["CharacterOfService"]);
        Assert.Equal("date||YES", columns["ServiceRecordVerifiedOn"]);
    }

    private static async Task AssertRejectedAsync(string constraint, Row row)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(row));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal(constraint, ex.ConstraintName);
    }

    private static async Task InsertAsync(Row row)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"Cases\" (\"Title\", \"Status\", \"CreatedAtUtc\", \"CreatedBy\", \"UpdatedAtUtc\", \"UpdatedBy\", " +
            "\"VeteranName\", \"VeteranAddress\", \"VeteranSsnLast4\", \"ServiceHistoryStatus\", \"ServiceBranch\", " +
            "\"ServiceStartDate\", \"ServiceEndDate\", \"CharacterOfService\", \"ServiceRecordVerifiedOn\") " +
            "VALUES ('Valid title', 0, now(), 'x', now(), 'x', @name, @address, @last4, @status, @branch, @start, @end, @character, @verified)",
            connection);
        cmd.Parameters.Add(Parameter("name", NpgsqlDbType.Varchar, row.WithVeteran ? "Pat Example" : null));
        cmd.Parameters.Add(Parameter("address", NpgsqlDbType.Varchar, row.WithVeteran ? "1 Example Way" : null));
        cmd.Parameters.Add(Parameter("last4", NpgsqlDbType.Char, row.WithVeteran ? "0000" : null));
        cmd.Parameters.Add(Parameter("status", NpgsqlDbType.Integer, row.Status));
        cmd.Parameters.Add(Parameter("branch", NpgsqlDbType.Varchar, row.Branch));
        cmd.Parameters.Add(Parameter("start", NpgsqlDbType.Date, row.StartDate));
        cmd.Parameters.Add(Parameter("end", NpgsqlDbType.Date, row.EndDate));
        cmd.Parameters.Add(Parameter("character", NpgsqlDbType.Varchar, row.Character));
        cmd.Parameters.Add(Parameter("verified", NpgsqlDbType.Date, row.VerifiedOn));
        await cmd.ExecuteNonQueryAsync();
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private sealed record Row
    {
        public static readonly Row FoundRow = new()
        {
            Status = Found, Branch = "Army", StartDate = Start, EndDate = End, Character = "Honorable", VerifiedOn = new DateOnly(2026, 1, 5),
        };

        public bool WithVeteran { get; init; } = true;
        public int? Status { get; init; }
        public string? Branch { get; init; }
        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public string? Character { get; init; }
        public DateOnly? VerifiedOn { get; init; }
    }
}
