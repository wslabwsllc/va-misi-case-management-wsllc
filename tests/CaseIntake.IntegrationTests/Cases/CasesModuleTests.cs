using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>The composed web application registers the real case module (not a fake) and it works end to end.</summary>
public class CasesModuleTests : ClassFixture<CasesModuleTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    [Test]
    public async Task Web_host_resolves_the_real_CaseService_per_scope()
    {
        var currentUser = new FakeCurrentUser();
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, configureServices: services =>
        {
            // Stand-in for E3's ICurrentUser adapter, which is not part of this epic.
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(currentUser);
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ICaseService>();
        Assert.IsType<CaseIntake.Cases.CaseService>(service);
        Assert.Same(CaseWorkflow.Instance, scope.ServiceProvider.GetRequiredService<ICaseWorkflow>());

        var created = CaseTestHarness.Ok(await service.CreateAsync(Builders.CreateCase(), CancellationToken.None));
        Assert.Equal(created.Id, CaseTestHarness.Ok(await service.GetAsync(created.Id, CancellationToken.None)).Id);

        await using var other = factory.Services.CreateAsyncScope();
        Assert.NotSame(service, other.ServiceProvider.GetRequiredService<ICaseService>());
    }
}
