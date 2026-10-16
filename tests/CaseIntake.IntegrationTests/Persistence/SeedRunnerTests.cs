using CaseIntake.Persistence;
using CaseIntake.Persistence.Seeding;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Persistence;

public class SeedRunnerTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{

    private ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(database.CreateContextFactory());
        register(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Contributors_run_in_Order_each_in_a_fresh_scope()
    {
        var order = new List<string>();
        await using var provider = Build(s =>
        {
            s.AddSingleton(order);
            s.AddScoped<ScopeMarker>();
            s.AddSeedContributor<Late>();
            s.AddSeedContributor<Early>();
        });

        await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);

        Assert.Equal(["Early", "Late"], order.Select(o => o.Split('|')[0]));
        Assert.NotEqual(order[0].Split('|')[1], order[1].Split('|')[1]);   // distinct DI scopes
    }

    [Fact]
    public async Task Each_contributor_is_constructed_once_per_run()
    {
        var constructions = new ConstructionCounter();
        await using var provider = Build(s =>
        {
            s.AddSingleton(constructions);
            s.AddSeedContributor<Counted>();
        });

        await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);

        Assert.Equal(1, constructions.Count);
    }

    [Fact]
    public async Task Running_twice_with_stable_keys_creates_no_duplicates()
    {
        await using var provider = Build(s => s.AddSeedContributor<RoleSeed>());
        var runner = new SeedRunner(provider, new FakeLogger<SeedRunner>());

        await runner.RunAsync(CancellationToken.None);
        await runner.RunAsync(CancellationToken.None);

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Roles.CountAsync(r => r.Id == "seed-role-1"));
    }

    [Fact]
    public async Task A_failing_contributor_fails_the_run()
    {
        await using var provider = Build(s => s.AddSeedContributor<Failing>());
        var logger = new FakeLogger<SeedRunner>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SeedRunner(provider, logger).RunAsync(CancellationToken.None));

        Assert.Contains(logger.Collector.GetSnapshot(), r => r.Id.Id == 1012);
    }

    private sealed class ScopeMarker { public string Id { get; } = Guid.NewGuid().ToString("N"); }

    private sealed class Early(List<string> order, ScopeMarker scope) : ISeedContributor
    {
        public static int Order => 10;
        public Task SeedAsync(CancellationToken ct) { order.Add($"Early|{scope.Id}"); return Task.CompletedTask; }
    }

    private sealed class Late(List<string> order, ScopeMarker scope) : ISeedContributor
    {
        public static int Order => 20;
        public Task SeedAsync(CancellationToken ct) { order.Add($"Late|{scope.Id}"); return Task.CompletedTask; }
    }

    private sealed class ConstructionCounter { public int Count; }

    private sealed class Counted : ISeedContributor
    {
        public Counted(ConstructionCounter counter) => Interlocked.Increment(ref counter.Count);
        public static int Order => 1;
        public Task SeedAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Failing : ISeedContributor
    {
        public static int Order => 1;
        public Task SeedAsync(CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    private sealed class RoleSeed(IDbContextFactory<AppDbContext> factory) : ISeedContributor
    {
        public static int Order => 5;
        public async Task SeedAsync(CancellationToken ct)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            if (!await db.Roles.AnyAsync(r => r.Id == "seed-role-1", ct))
            {
                db.Roles.Add(new AppRole { Id = "seed-role-1", Name = "SeedRole", NormalizedName = "SEEDROLE" });
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
