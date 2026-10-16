using CaseIntake.Cases;
using CaseIntake.Cases.Seeding;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Seeding;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-S9 (with E1-S4): the case seed is complete, consistent, idempotent and deterministic.</summary>
public sealed class CasesSeedTests : IAsyncLifetime
{
    private readonly PostgresDatabaseFixture _first = new();
    private readonly PostgresDatabaseFixture _second = new();

    public Task InitializeAsync() => Task.WhenAll(_first.InitializeAsync(), _second.InitializeAsync());

    public Task DisposeAsync() => Task.WhenAll(_first.DisposeAsync(), _second.DisposeAsync());

    private static async Task SeedAsync(PostgresDatabaseFixture database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(database.CreateContextFactory());
        services.AddCasesSeed();
        await using var provider = services.BuildServiceProvider();
        await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Seed_creates_at_least_60_cases_in_every_status_with_consistent_audit_trails()
    {
        await SeedAsync(_first);

        await using var db = _first.CreateContext();
        var cases = await db.Cases.AsNoTracking().ToListAsync();
        var audit = await db.CaseAuditEntries.AsNoTracking().ToListAsync();
        Assert.True(cases.Count >= 60, $"{cases.Count} cases");
        Assert.All(Enum.GetValues<CaseStatus>(), s => Assert.Contains(cases, c => c.Status == s));
        foreach (var c in cases)
        {
            var trail = audit.Where(a => a.CaseId == c.Id).OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id).ToList();
            Assert.Equal(AuditActions.Created, trail[0].Action);
            Assert.Equal(c.CreatedAtUtc, trail[0].OccurredAtUtc);
            Assert.Equal(c.UpdatedAtUtc, trail[^1].OccurredAtUtc);
            Assert.Equal(c.Status, trail.LastOrDefault(a => a.ToStatus is not null)!.ToStatus);
            Assert.Equal(TimeSpan.Zero, c.CreatedAtUtc.Offset);
            Assert.NotNull(c.SeedKey);
            Assert.Null(c.Veteran);
        }
        Assert.Contains(audit, a => a.Action == AuditActions.Reopened);
        Assert.Contains(audit, a => a.Action == AuditActions.Edited);
        Assert.Contains(audit, a => a.Actor == "supervisor@example.test" && a.ToStatus == CaseStatus.Closed);
    }

    [Fact]
    public async Task Reseeding_adds_nothing_and_keeps_user_edits()
    {
        await SeedAsync(_first);
        long editedId;
        await using (var db = _first.CreateContext())
        {
            var entity = await db.Cases.OrderBy(c => c.Id).FirstAsync(c => c.Status != CaseStatus.Closed);
            entity.Edit("Edited by an evaluator", null, "evaluator", CaseTestHarness.StartTime);
            await db.SaveChangesAsync();
            editedId = entity.Id;
        }
        var (casesBefore, auditBefore) = await CountsAsync(_first);

        await SeedAsync(_first);

        Assert.Equal((casesBefore, auditBefore), await CountsAsync(_first));
        await using var check = _first.CreateContext();
        Assert.Equal("Edited by an evaluator", (await check.Cases.SingleAsync(c => c.Id == editedId)).Title);
    }

    [Fact]
    public async Task Partially_seeded_database_is_completed_not_skipped()
    {
        await SeedAsync(_first);
        await using (var db = _first.CreateContext())
        {
            // Simulate a database seeded by an older, shorter fixture set.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"CaseAuditEntries\" WHERE \"CaseId\" IN (SELECT \"Id\" FROM \"Cases\" WHERE \"SeedKey\" >= 'case-061')");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Cases\" WHERE \"SeedKey\" >= 'case-061'");
        }

        await SeedAsync(_first);

        Assert.Equal(CaseSeedData.CaseCount, (await CountsAsync(_first)).Cases);
    }

    [Fact]
    public async Task Two_fresh_databases_receive_identical_business_data()
    {
        await Task.WhenAll(SeedAsync(_first), SeedAsync(_second));

        Assert.Equal(await SnapshotAsync(_first), await SnapshotAsync(_second));
    }

    [Fact]
    public async Task Audit_entries_reference_seeded_Identity_users_when_present()
    {
        await using (var db = _first.CreateContext())
        {
            db.Users.Add(new AppUser { Id = "supervisor-guid", UserName = "supervisor@example.test", NormalizedUserName = "SUPERVISOR@EXAMPLE.TEST" });
            await db.SaveChangesAsync();
        }

        await SeedAsync(_first);

        await using var check = _first.CreateContext();
        var supervisorEntries = await check.CaseAuditEntries.Where(a => a.Actor == "supervisor@example.test").ToListAsync();
        Assert.NotEmpty(supervisorEntries);
        Assert.All(supervisorEntries, a => Assert.Equal("supervisor-guid", a.ActorId));
        Assert.All(await check.CaseAuditEntries.Where(a => a.Actor == "caseworker@example.test").ToListAsync(),
            a => Assert.Equal("seed:caseworker@example.test", a.ActorId));
    }

    private static async Task<(int Cases, int Audit)> CountsAsync(PostgresDatabaseFixture database)
    {
        await using var db = database.CreateContext();
        return (await db.Cases.CountAsync(), await db.CaseAuditEntries.CountAsync());
    }

    /// <summary>Stable business data keyed by seed key; database-generated IDs and row versions are excluded.</summary>
    private static async Task<List<string>> SnapshotAsync(PostgresDatabaseFixture database)
    {
        await using var db = database.CreateContext();
        var cases = await db.Cases.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var rows = cases.Values.Select(c =>
            $"{c.SeedKey}|{c.Title}|{c.Description}|{c.Status}|{c.CreatedAtUtc:O}|{c.CreatedBy}|{c.UpdatedAtUtc:O}|{c.UpdatedBy}").ToList();
        rows.AddRange((await db.CaseAuditEntries.AsNoTracking().ToListAsync()).Select(a =>
            $"{cases[a.CaseId].SeedKey}|{a.OccurredAtUtc:O}|{a.ActorId}|{a.Actor}|{a.Action}|{a.FromStatus}|{a.ToStatus}|{a.Details}"));
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }
}
