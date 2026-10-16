using CaseIntake.Persistence;
using CaseIntake.TestKit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.IntegrationTests.Persistence;

public class ContextLifetimeTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddPersistence(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:CaseIntake"] = database.ConnectionString }).Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Simultaneous_operations_receive_distinct_disposable_contexts()
    {
        await using var provider = BuildServices();
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var first = await factory.CreateDbContextAsync();
        await using var second = await factory.CreateDbContextAsync();

        Assert.NotSame(first, second);
        await Task.WhenAll(first.Users.CountAsync(), second.Users.CountAsync());
        await first.DisposeAsync();
        Assert.Equal(0, await second.Users.CountAsync());   // disposing one does not affect the other
    }

    [Fact]
    public async Task A_failed_save_commits_none_of_the_batch()
    {
        // One SaveChanges is one transaction: a constraint violation on the second row must roll back the first.
        // (The case-plus-audit variant of this guarantee is verified by E2-S3/E2-S4 against the real CaseService.)
        await using var db = database.CreateContext();
        var role = new AppRole { Id = "r-atomic", Name = "Atomic", NormalizedName = "ATOMIC" };
        var duplicate = new AppRole { Id = "r-atomic-2", Name = "Atomic", NormalizedName = "ATOMIC" };
        db.Roles.Add(role);
        db.Roles.Add(duplicate);

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        await using var check = database.CreateContext();
        Assert.False(await check.Roles.AnyAsync(r => r.Id == "r-atomic"));
    }
}
