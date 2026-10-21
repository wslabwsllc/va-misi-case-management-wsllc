using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;
using CaseIntake.ServiceDefaults;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-S2/E2-S3: intake validation, persistence, audit information, permissions and logging.</summary>
public class CaseIntakeTests : ClassFixture<CaseIntakeTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    [Test]
    public async Task Valid_intake_is_saved_as_New_with_utc_audit_fields_and_a_Created_entry()
    {
        var harness = new CaseTestHarness(database) { Actor = TestActors.CaseWorker };

        var created = CaseTestHarness.Ok(await harness.Service.CreateAsync(
            new CreateCaseRequest("  Leaking roof at clinic  ", "Synthetic description.", Builders.Veteran()), CancellationToken.None));

        Assert.True(created.Id > 0);
        Assert.Equal("Leaking roof at clinic", created.Title);
        Assert.Equal(CaseStatus.New, created.Status);
        Assert.Equal(CaseTestHarness.StartTime, created.CreatedAtUtc);
        Assert.Equal(created.CreatedAtUtc, created.UpdatedAtUtc);
        Assert.Equal("caseworker@example.test", created.CreatedBy);
        Assert.Equal(created.CreatedBy, created.UpdatedBy);
        Assert.NotEqual(0u, created.Version);
        Assert.Empty(created.AllowedNextStatuses);   // CaseWorker cannot triage

        var stored = await harness.LoadAsync(created.Id);
        Assert.Equal(TimeSpan.Zero, stored.CreatedAtUtc.Offset);
        Assert.Equal(created.Version, stored.Version);

        var audit = Assert.Single(CaseTestHarness.Ok(await harness.Service.GetAuditTrailAsync(created.Id, CancellationToken.None)));
        Assert.Equal(AuditActions.Created, audit.Action);
        Assert.Equal("caseworker@example.test", audit.Actor);
        Assert.Equal(CaseTestHarness.StartTime, audit.OccurredAtUtc);
        Assert.Null(audit.FromStatus);
        Assert.Equal(CaseStatus.New, audit.ToStatus);
        await using var db = database.CreateContext();
        Assert.Equal("cw-id", (await db.CaseAuditEntries.SingleAsync(a => a.CaseId == created.Id)).ActorId);
    }

    [Test]
    public async Task Veteran_snapshot_is_stored_but_never_a_full_ssn()
    {
        var harness = new CaseTestHarness(database);

        var created = await harness.CreateAsync(veteran: new VeteranSnapshot("Pat Example", "1 Example Way, Testville", "3456"));

        Assert.Equal(new VeteranSnapshot("Pat Example", "1 Example Way, Testville", "3456"), created.Veteran);
        Assert.Equal(created.Veteran, CaseTestHarness.Ok(await harness.Service.GetAsync(created.Id, CancellationToken.None)).Veteran);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT row_to_json(c)::text FROM \"Cases\" c WHERE \"Id\" = {created.Id}", connection);
        var row = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.DoesNotMatch(@"\d{3}-?\d{2}-?\d{4}", row);
        Assert.Contains("\"VeteranSsnLast4\":\"3456\"", row, StringComparison.Ordinal);
    }

    [TestCase("abcd", null)]
    [TestCase("", null)]
    [TestCase("Valid title", "D4001")]
    public async Task Invalid_intake_returns_field_errors_and_saves_nothing(string title, string? description)
    {
        description = description == "D4001" ? new string('d', 4001) : description;
        var harness = new CaseTestHarness(database);
        var before = await CountCasesAsync();

        var error = CaseTestHarness.Fail(await harness.Service.CreateAsync(new CreateCaseRequest(title, description, Builders.Veteran()), CancellationToken.None), ErrorKind.Validation);

        Assert.NotEmpty(error.FieldErrors!);
        Assert.Equal(before, await CountCasesAsync());
    }

    [Test]
    public async Task Invalid_veteran_snapshot_is_rejected_without_persistence()
    {
        var harness = new CaseTestHarness(database);
        var before = await CountCasesAsync();

        var error = CaseTestHarness.Fail(await harness.Service.CreateAsync(
            new CreateCaseRequest("Valid title", null, new VeteranSnapshot(new string('n', 201), "1 Example Way", "12-4")), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal([CaseFieldNames.VeteranName, CaseFieldNames.VeteranSsnLast4], error.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(before, await CountCasesAsync());
    }

    [Test]
    public async Task A_case_without_a_veteran_is_rejected_without_persistence()
    {
        var harness = new CaseTestHarness(database);
        var before = await CountCasesAsync();

        var error = CaseTestHarness.Fail(await harness.Service.CreateAsync(
            new CreateCaseRequest("Valid title", null, null), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal([CaseFieldNames.Veteran], error.FieldErrors!.Keys);
        Assert.Equal(before, await CountCasesAsync());
    }

    [Test]
    public async Task Users_without_create_permission_are_forbidden_and_nothing_is_saved()
    {
        var harness = new CaseTestHarness(database) { Actor = TestActors.ViewOnly };
        var before = await CountCasesAsync();

        CaseTestHarness.Fail(await harness.Service.CreateAsync(Builders.CreateCase(), CancellationToken.None), ErrorKind.Forbidden);
        harness.Actor = TestKit.Fakes.FakeCurrentUser.Anonymous;
        CaseTestHarness.Fail(await harness.Service.CreateAsync(Builders.CreateCase(), CancellationToken.None), ErrorKind.Forbidden);

        Assert.Equal(before, await CountCasesAsync());
        Assert.Equal(2, harness.Logs.Collector.GetSnapshot().Count(r => r.Id.Id == SecurityEvents.AuthorizationDeniedId));
    }

    [Test]
    public async Task CaseCreated_is_logged_with_id_and_actor_but_no_case_text()
    {
        var harness = new CaseTestHarness(database);

        var created = await harness.CreateAsync("Confidential sounding title", "Secret description content",
            new VeteranSnapshot("Pat Example", "1 Example Way", "3456"));

        var record = Assert.Single(harness.Logs.Collector.GetSnapshot(), r => r.Id.Id == 2001);
        Assert.Equal("CaseCreated", record.Id.Name);
        Assert.Contains(record.StructuredState!, kv => kv.Key == "CaseId" && kv.Value == created.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(record.StructuredState!, kv => kv.Key == "ActorId" && kv.Value == "sv-id");
        foreach (var r in harness.Logs.Collector.GetSnapshot())
        {
            Assert.DoesNotContain("Secret description", r.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Confidential", r.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Pat Example", r.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("3456", r.Message, StringComparison.Ordinal);
            Assert.False(SsnRedaction.ContainsSsn(r.Message));
        }
    }

    /// <summary>E1-S3: a forced database failure commits neither the case nor its audit entry (one SaveChanges, one transaction).</summary>
    [Test]
    public async Task Failed_save_commits_neither_the_case_nor_its_audit_entry()
    {
        var harness = new CaseTestHarness(database);
        var casesBefore = await CountCasesAsync();
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                CREATE FUNCTION fail_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'forced audit failure'; END $$;
                CREATE TRIGGER fail_audit BEFORE INSERT ON "CaseAuditEntries" FOR EACH ROW EXECUTE FUNCTION fail_audit();
                """, connection);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => harness.Service.CreateAsync(Builders.CreateCase(), CancellationToken.None));
            Assert.Equal(casesBefore, await CountCasesAsync());
        }
        finally
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand("DROP TRIGGER fail_audit ON \"CaseAuditEntries\"; DROP FUNCTION fail_audit();", connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<int> CountCasesAsync()
    {
        await using var db = database.CreateContext();
        return await db.Cases.CountAsync();
    }
}
