using CaseIntake.Contracts.Cases;
using CaseIntake.Domain.Cases;
using CaseIntake.ServiceDefaults;
using CaseIntake.TestKit;
using Npgsql;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>
/// E3-C2-2: a case created with each service-history outcome stores the last four SSN digits only. Every row of the
/// case, case-audit and system-audit tables of this database is read as JSON and checked for an SSN-shaped value and
/// for the rest of the SSN the lookups used, so a new column or audit detail that leaked one would fail here.
/// </summary>
[Category("Story:E3-C2-2")]
[Category("Req:R-CR2-8")]
public class ServiceHistoryStorageTests : ClassFixture<ServiceHistoryStorageTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    private static readonly string[] Tables = ["Cases", "CaseAuditEntries", "AuditLogEntries"];

    private static readonly ServiceHistory Army =
        new("Army", new DateOnly(2001, 2, 15), new DateOnly(2005, 2, 14), "Honorable", new DateOnly(2026, 1, 5));

    [Test]
    public async Task No_full_SSN_is_stored_in_case_or_audit_rows_for_any_service_history_outcome()
    {
        var harness = new CaseTestHarness(database);
        var created = new List<CaseDetailDto>();
        foreach (var (status, history) in new (ServiceHistoryStatus?, ServiceHistory?)[]
        {
            (ServiceHistoryStatus.Found, Army),
            (ServiceHistoryStatus.Found, Army with { ServiceEndDate = null, LastVerifiedOn = null }),
            (ServiceHistoryStatus.NotFound, null),
            (ServiceHistoryStatus.Unavailable, null),
            (null, null),
        })
        {
            // The demo veteran 900-12-3456: only "3456" may be stored.
            created.Add(await harness.CreateAsync(veteran: new VeteranSnapshot("Rowan Calloway", "6243 Placeholder Street", "3456", status, history)));
        }

        var rows = await ReadRowsAsync();

        Assert.Equal(created.Count, rows["Cases"].Count);
        Assert.NotEmpty(rows["CaseAuditEntries"]);
        foreach (var (table, json) in rows.SelectMany(t => t.Value.Select(row => (t.Key, row))))
        {
            Assert.False(SsnRedaction.ContainsSsn(json), $"{table} holds an SSN-shaped value");
            Assert.DoesNotContain("900-12", json, StringComparison.Ordinal);
            Assert.DoesNotContain("90012", json, StringComparison.Ordinal);
        }
        Assert.All(rows["Cases"], json => Assert.Contains("\"VeteranSsnLast4\":\"3456\"", json, StringComparison.Ordinal));
        // The history itself is stored as case data (R-CR2-2); the audit entry names the status only (E2-C2-3).
        Assert.Contains(rows["Cases"], json => json.Contains("\"ServiceBranch\":\"Army\"", StringComparison.Ordinal));
        Assert.All(rows["CaseAuditEntries"].Concat(rows["AuditLogEntries"]), json =>
        {
            Assert.DoesNotContain("Army", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Honorable", json, StringComparison.Ordinal);
        });
    }

    private static async Task<Dictionary<string, List<string>>> ReadRowsAsync()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var rows = new Dictionary<string, List<string>>();
        foreach (var table in Tables)
        {
            // Table names come from the constant list above, never from input.
            await using var cmd = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM \"{table}\" t", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<string>();
            while (await reader.ReadAsync())
            {
                list.Add(reader.GetString(0));
            }
            rows[table] = list;
        }
        return rows;
    }
}
