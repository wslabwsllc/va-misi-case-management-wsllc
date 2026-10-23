using System.Net;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Auth;
using CaseIntake.TestKit.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace CaseIntake.IntegrationTests.TestKitTests;

public class TestKitTests
{
    [Test]
    public async Task Each_fixture_gets_its_own_database_that_is_dropped_on_disposal()
    {
        var a = new PostgresDatabaseFixture();
        var b = new PostgresDatabaseFixture();
        await Task.WhenAll(a.InitializeAsync(), b.InitializeAsync());

        Assert.NotEqual(a.DatabaseName, b.DatabaseName);
        await using (var db = a.CreateContext()) { Assert.Empty(db.Users); }
        await a.DisposeAsync();
        await b.DisposeAsync();

        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(a.ConnectionString) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM pg_database WHERE datname IN ('{a.DatabaseName}','{b.DatabaseName}')", admin);
        Assert.Equal(0L, await cmd.ExecuteScalarAsync());
    }

    [Test]
    public async Task Reset_removes_data_but_keeps_the_schema()
    {
        var fixture = new PostgresDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using (var db = fixture.CreateContext())
            {
                db.Roles.Add(new CaseIntake.Persistence.AppRole { Id = "r1", Name = "R", NormalizedName = "R" });
                await db.SaveChangesAsync();
            }

            await fixture.ResetAsync();

            await using var check = fixture.CreateContext();
            Assert.Empty(check.Roles);
            Assert.Empty(await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions
                .GetPendingMigrationsAsync(check.Database));
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Test]
    public async Task TestAuthHandler_authenticates_by_header_and_is_anonymous_without_one()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.MapGet("/who", (System.Security.Claims.ClaimsPrincipal user) =>
            $"{user.Identity?.IsAuthenticated}|{user.Identity?.Name}|{user.IsInRole(AppRoles.Supervisor)}");
        await app.StartAsync();

        using var anon = app.GetTestClient();
        using var supervisor = app.GetTestClient().WithUser(AppRoles.Supervisor, "sup-1");

        Assert.Equal("False||False", await anon.GetStringAsync("/who"));
        Assert.Equal("True|sup-1|True", await supervisor.GetStringAsync("/who"));
    }

    [Test]
    public async Task Fake_modules_follow_the_workflow_edges_and_permissions()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));
        var user = new FakeCurrentUser();
        var cases = new InMemoryCaseService(user, time);

        var created = (await cases.CreateAsync(Builders.CreateCase(), default)).Value!;
        Assert.Equal(CaseStatus.New, created.Status);
        Assert.Equal(time.GetUtcNow(), created.CreatedAtUtc);

        var bad = await cases.ChangeStatusAsync(created.Id, new ChangeStatusRequest(CaseStatus.Resolved, created.Version), default);
        Assert.Equal(CaseIntake.Contracts.Common.ErrorKind.InvalidTransition, bad.Error!.Kind);

        user.Actor = FakeCurrentUser.Authenticated("cw", "Case Worker", AppRoles.CaseWorker, [Permissions.CasesView]);
        var denied = await cases.ChangeStatusAsync(created.Id, new ChangeStatusRequest(CaseStatus.Triaged, created.Version), default);
        Assert.Equal(CaseIntake.Contracts.Common.ErrorKind.Forbidden, denied.Error!.Kind);

        user.Actor = FakeCurrentUser.Anonymous;
        Assert.False(user.Actor.HasPermission(Permissions.CasesView));
    }

    [Test]
    public async Task Fake_lookup_returns_scripted_answers_and_NotFound_otherwise()
    {
        var fake = new FakeVeteranLookupClient().Returns("900223333", new VeteranLookupResult(VeteranLookupOutcome.Found, "Pat Example", "1 Way"));

        Assert.Equal(VeteranLookupOutcome.Found, (await fake.LookupAsync("900223333", default)).Outcome);
        Assert.Equal(VeteranLookupOutcome.NotFound, (await fake.LookupAsync("000000000", default)).Outcome);
        Assert.Equal(2, fake.CallCount);
    }

    [Test]
    public async Task Fake_modules_register_the_service_history_fake_which_answers_NotFound_by_default()
    {
        await using var provider = new ServiceCollection().AddFakeModules().BuildServiceProvider();
        var client = provider.GetRequiredService<IVeteranServiceHistoryClient>();

        Assert.IsType<FakeVeteranServiceHistoryClient>(client);
        Assert.Equal(new ServiceHistoryResult(VeteranLookupOutcome.NotFound, null), await client.LookupAsync("900223333", default));
    }
}
