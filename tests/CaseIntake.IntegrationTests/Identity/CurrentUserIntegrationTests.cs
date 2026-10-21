using System.Net;
using System.Security.Claims;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Seeding;
using CaseIntake.Persistence;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>
/// E3-S3 in the composed web host: each circuit scope sees only its own user, HTTP requests see their own principal,
/// and revalidation revokes a circuit whose user's security stamp changed.
/// </summary>
public class CurrentUserIntegrationTests : ClassFixture<CurrentUserIntegrationTests, IdentityWebFixture>
{
    private static IdentityWebFixture fixture => Shared;

    private async Task<ClaimsPrincipal> PrincipalForAsync(string userName)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var claims = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
        return await claims.CreateAsync((await users.FindByNameAsync(userName))!);
    }

    /// <summary>A circuit's DI scope with its authentication state set the way the Blazor host sets it.</summary>
    private async Task<AsyncServiceScope> CircuitAsync(string userName)
    {
        var scope = fixture.Factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        ((IHostEnvironmentAuthenticationStateProvider)provider).SetAuthenticationState(
            Task.FromResult(new AuthenticationState(await PrincipalForAsync(userName))));
        return scope;
    }

    [Test]
    public async Task Simultaneous_circuits_and_an_HTTP_request_each_observe_only_their_own_user()
    {
        await using var caseWorkerCircuit = await CircuitAsync(DemoUsers.CaseWorker.UserName);
        await using var supervisorCircuit = await CircuitAsync(DemoUsers.Supervisor.UserName);
        using var session = fixture.NewSession();
        await session.SignInAsync(IdentityWebFixture.NoPermissionsUser);

        var actors = await Task.WhenAll(
            caseWorkerCircuit.ServiceProvider.GetRequiredService<ICurrentUser>().GetAsync(CancellationToken.None),
            supervisorCircuit.ServiceProvider.GetRequiredService<ICurrentUser>().GetAsync(CancellationToken.None));
        using var http = await session.Client.GetAsync("/");   // the no-permission user's request is denied on its own principal

        Assert.Equal(DemoUsers.CaseWorker.Id, actors[0].UserId);
        Assert.Equal([AppRoles.CaseWorker], actors[0].Roles);
        Assert.Equal(DemoUsers.Supervisor.Id, actors[1].UserId);
        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.Supervisor]), actors[1].Permissions);
        Assert.Equal(HttpStatusCode.Redirect, http.StatusCode);
        Assert.Contains("/account/access-denied", http.Headers.Location!.ToString());
    }

    [Test]
    public async Task Services_in_a_circuit_act_as_the_circuit_user()
    {
        await using var circuit = await CircuitAsync(DemoUsers.CaseWorker.UserName);
        var cases = circuit.ServiceProvider.GetRequiredService<ICaseService>();

        var created = await cases.CreateAsync(TestKit.Builders.CreateCase(), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(DemoUsers.CaseWorker.UserName, created.Value!.CreatedBy);
        var triage = await cases.ChangeStatusAsync(created.Value.Id,
            new ChangeStatusRequest(Domain.Cases.CaseStatus.Triaged, created.Value.Version), CancellationToken.None);
        Assert.Equal(ErrorKind.Forbidden, triage.Error?.Kind);
    }

    [Test]
    public async Task Revalidation_signs_out_a_circuit_whose_user_changed_so_later_operations_are_denied()
    {
        await using var circuit = await CircuitAsync(IdentityWebFixture.RevalidationUser);
        var provider = circuit.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var currentUser = circuit.ServiceProvider.GetRequiredService<ICurrentUser>();
        var cases = circuit.ServiceProvider.GetRequiredService<ICaseService>();
        Assert.True((await currentUser.GetAsync(CancellationToken.None)).HasPermission(Permissions.CasesView));
        Assert.True((await cases.ListAsync(new CaseListQuery(), CancellationToken.None)).IsSuccess);

        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AuthenticationStateChanged += async state =>
        {
            if ((await state).User.Identity?.IsAuthenticated != true)
            {
                revoked.TrySetResult();
            }
        };
        await using (var admin = fixture.Factory.Services.CreateAsyncScope())
        {
            var users = admin.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.UpdateSecurityStampAsync((await users.FindByNameAsync(IdentityWebFixture.RevalidationUser))!)).Succeeded);
        }
        await revoked.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var after = await currentUser.GetAsync(CancellationToken.None);
        Assert.False(after.IsAuthenticated);
        Assert.Equal(ErrorKind.Forbidden, (await cases.ListAsync(new CaseListQuery(), CancellationToken.None)).Error?.Kind);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3006);
    }
}
