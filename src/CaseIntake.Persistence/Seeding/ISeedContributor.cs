namespace CaseIntake.Persistence.Seeding;

/// <summary>
/// Seeds one slice of deterministic synthetic data. Each contributor runs in its own DI scope, resolves its own
/// context factory or Identity stores, owns its transaction, keys its rows by stable seed keys so a partially
/// completed run is completed rather than skipped, and throws on any failure so the migrator exits non-zero.
/// </summary>
public interface ISeedContributor
{
    /// <summary>Execution order (ascending). Static so the runner can order contributors without constructing them.</summary>
    static abstract int Order { get; }

    Task SeedAsync(CancellationToken ct);
}
