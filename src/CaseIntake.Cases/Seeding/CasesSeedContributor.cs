using System.Collections.Immutable;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using CaseIntake.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Cases.Seeding;

/// <summary>
/// Seeds the synthetic demo cases with consistent audit trails (E2-S9). Runs after the Identity seed (order 10) so
/// audit entries reference the seeded demo users' IDs. Fixtures are keyed by <see cref="Case.SeedKey"/>: missing
/// keys are added, existing ones are never touched, so re-running neither duplicates data nor overwrites user edits.
/// All additions commit in one transaction.
/// </summary>
public sealed class CasesSeedContributor(
    IDbContextFactory<AppDbContext> contextFactory,
    ICaseWorkflow workflow,
    ILogger<CasesSeedContributor> logger) : ISeedContributor
{
    public static int Order => 20;

    public async Task SeedAsync(CancellationToken ct)
    {
        var fixtures = CaseSeedData.Build();
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var strategy = db.Database.CreateExecutionStrategy();
        var added = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var existing = (await db.Cases.Where(c => c.SeedKey != null).Select(c => c.SeedKey!).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            var actors = await ResolveActorsAsync(db, ct);

            var count = 0;
            foreach (var fixture in fixtures.Where(f => !existing.Contains(f.Key)))
            {
                AddFixture(db, fixture, actors);
                count++;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return count;
        });

        CaseLog.CasesSeeded(logger, added, fixtures.Count);
    }

    private void AddFixture(AppDbContext db, SeedCase fixture, IReadOnlyDictionary<string, OperationActor> actors)
    {
        var creator = actors[fixture.CreatorUserName];
        var entity = Case.Create(fixture.Title, fixture.Description, veteran: null,
            AuditAppender.ActorName(creator), fixture.CreatedAtUtc, fixture.Key);
        db.Cases.Add(entity);
        AuditAppender.Append(db, entity, creator, AuditEvent.Created(fixture.CreatedAtUtc, hasVeteranSnapshot: false));

        foreach (var step in fixture.Steps)
        {
            var actor = actors[step.ActorUserName];
            var actorName = AuditAppender.ActorName(actor);
            if (step.TargetStatus is { } target)
            {
                var from = entity.TransitionTo(target, workflow, actorName, step.AtUtc);
                AuditAppender.Append(db, entity, actor, AuditEvent.StatusChange(step.AtUtc, from, target));
            }
            else
            {
                var changed = entity.Edit(entity.Title, step.EditedDescription, actorName, step.AtUtc);
                AuditAppender.Append(db, entity, actor, AuditEvent.Edited(step.AtUtc, changed));
            }
        }
    }

    /// <summary>
    /// Seeded demo users by user name. When the Identity seed has not created a user (for example in an isolated
    /// test database) a stable synthetic ID is used so the fixture data stays deterministic.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, OperationActor>> ResolveActorsAsync(AppDbContext db, CancellationToken ct)
    {
        string[] userNames = [CaseSeedData.CaseWorkerUserName, CaseSeedData.SupervisorUserName];
        var normalized = userNames.Select(n => n.ToUpperInvariant()).ToArray();
        var ids = await db.Users.Where(u => u.NormalizedUserName != null && normalized.Contains(u.NormalizedUserName))
            .ToDictionaryAsync(u => u.NormalizedUserName!, u => u.Id, ct);

        return userNames.ToDictionary(
            n => n,
            n => new OperationActor(ids.GetValueOrDefault(n.ToUpperInvariant()) ?? $"seed:{n}", n,
                ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty),
            StringComparer.Ordinal);
    }
}
