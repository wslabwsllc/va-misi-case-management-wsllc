using CaseIntake.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Seeding;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Seeding;
using CaseIntake.TestKit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>E3-S4: roles and one demo user per role, idempotent, passwords never reset.</summary>
public sealed class IdentitySeedTests : ClassFixture<IdentitySeedTests, PostgresDatabaseFixture>, IAsyncLifetime
{
    private static PostgresDatabaseFixture database => Shared;

    [SetUp]

    public Task InitializeAsync() => database.ResetAsync();

    [TearDown]

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> WithUserManagerAsync<T>(Func<UserManager<AppUser>, Task<T>> action)
    {
        await using var provider = IdentityTestSupport.SeedServices(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
    }

    [Test]
    public async Task A_fresh_seed_creates_both_roles_and_one_user_per_role_with_the_configured_password()
    {
        await IdentityTestSupport.SeedAsync(database.ConnectionString);

        await using var db = database.CreateContext();
        Assert.Equal(RolePermissionMap.Roles.Order(StringComparer.Ordinal), await db.Roles.Select(r => r.Name!).OrderBy(n => n).ToListAsync());
        var memberships = await (from ur in db.UserRoles
                                 join u in db.Users on ur.UserId equals u.Id
                                 join r in db.Roles on ur.RoleId equals r.Id
                                 select new { u.Id, u.UserName, Role = r.Name }).ToListAsync();
        Assert.Equal(DemoUsers.All.Count, memberships.Count);
        foreach (var demo in DemoUsers.All)
        {
            var row = Assert.Single(memberships, m => m.UserName == demo.UserName);
            Assert.Equal(demo.Id, row.Id);
            Assert.Equal(demo.Role, row.Role);
            Assert.EndsWith(".test", demo.UserName, StringComparison.Ordinal);
        }

        await WithUserManagerAsync(async users =>
        {
            foreach (var demo in DemoUsers.All)
            {
                var user = (await users.FindByNameAsync(demo.UserName))!;
                Assert.True(await users.CheckPasswordAsync(user, IdentityTestSupport.DemoPassword));
                Assert.True(user.EmailConfirmed);
                Assert.True(user.LockoutEnabled);
            }
            return 0;
        });
    }

    [Test]
    public async Task Reseeding_creates_no_duplicates_and_does_not_reset_passwords()
    {
        await IdentityTestSupport.SeedAsync(database.ConnectionString);
        const string changed = "Changed-Passw0rd!";
        await WithUserManagerAsync(async users =>
        {
            var user = (await users.FindByNameAsync(DemoUsers.CaseWorker.UserName))!;
            Assert.True((await users.ChangePasswordAsync(user, IdentityTestSupport.DemoPassword, changed)).Succeeded);
            return 0;
        });

        await IdentityTestSupport.SeedAsync(database.ConnectionString);
        await IdentityTestSupport.SeedAsync(database.ConnectionString, demoPassword: "Another-Passw0rd!");

        await using (var db = database.CreateContext())
        {
            Assert.Equal(RolePermissionMap.Roles.Length, await db.Roles.CountAsync());
            Assert.Equal(DemoUsers.All.Count, await db.Users.CountAsync());
            Assert.Equal(DemoUsers.All.Count, await db.UserRoles.CountAsync());
        }
        await WithUserManagerAsync(async users =>
        {
            var user = (await users.FindByNameAsync(DemoUsers.CaseWorker.UserName))!;
            Assert.True(await users.CheckPasswordAsync(user, changed));
            Assert.False(await users.CheckPasswordAsync(user, IdentityTestSupport.DemoPassword));
            return 0;
        });
    }

    [Test]
    public async Task A_partially_seeded_database_is_completed()
    {
        // Simulates a run that stopped after creating the case worker but before adding its role membership.
        await WithUserManagerAsync(async users =>
        {
            var user = new AppUser { Id = DemoUsers.CaseWorker.Id, UserName = DemoUsers.CaseWorker.UserName, Email = DemoUsers.CaseWorker.UserName };
            Assert.True((await users.CreateAsync(user, "Partial-Passw0rd!")).Succeeded);
            return 0;
        });

        await IdentityTestSupport.SeedAsync(database.ConnectionString);

        await WithUserManagerAsync(async users =>
        {
            var caseWorker = (await users.FindByNameAsync(DemoUsers.CaseWorker.UserName))!;
            Assert.Equal([AppRoles.CaseWorker], await users.GetRolesAsync(caseWorker));
            Assert.True(await users.CheckPasswordAsync(caseWorker, "Partial-Passw0rd!"));
            Assert.Equal([AppRoles.Supervisor], await users.GetRolesAsync((await users.FindByNameAsync(DemoUsers.Supervisor.UserName))!));
            return 0;
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task A_missing_demo_password_fails_the_seed_and_creates_no_users(string? password)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => IdentityTestSupport.SeedAsync(database.ConnectionString, password));

        Assert.Contains("Seed:DemoPassword", ex.Message);
        await using var db = database.CreateContext();
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Test]
    public async Task A_password_that_violates_the_policy_fails_the_seed()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => IdentityTestSupport.SeedAsync(database.ConnectionString, "short"));

        Assert.DoesNotContain("short", ex.Message.Replace("PasswordTooShort", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Test]
    public async Task Identity_seeds_before_cases_so_seeded_audit_entries_reference_real_user_ids()
    {
        await using (var provider = IdentityTestSupport.SeedServices(database.ConnectionString, extra: s => s.AddCasesSeed()))
        {
            await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);
        }

        await using var db = database.CreateContext();
        var actorIds = await db.CaseAuditEntries.Select(a => a.ActorId).Distinct().ToListAsync();
        Assert.NotEmpty(actorIds);
        Assert.All(actorIds, id => Assert.Contains(id, DemoUsers.All.Select(u => u.Id)));
    }
}
