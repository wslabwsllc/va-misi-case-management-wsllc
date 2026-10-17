using System.Security.Claims;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Identity.CurrentUser;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;

namespace CaseIntake.Identity.Tests;

public class CurrentUserAdapterTests
{
    /// <summary>A circuit's provider whose state arrives asynchronously, like a real circuit.</summary>
    private sealed class DeferredStateProvider : AuthenticationStateProvider
    {
        public TaskCompletionSource<AuthenticationState> State { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => State.Task;
    }

    private static ServerAuthenticationStateProvider Circuit(ClaimsPrincipal user)
    {
        var provider = new ServerAuthenticationStateProvider();
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));
        return provider;
    }

    private static HttpCurrentUser Http(ClaimsPrincipal user) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } });

    [Fact]
    public async Task Circuit_adapter_reflects_the_circuit_principal()
    {
        var actor = await new CircuitCurrentUser(Circuit(Principals.User("sv-1", "supervisor@example.test", AppRoles.Supervisor)))
            .GetAsync(CancellationToken.None);

        Assert.Equal("sv-1", actor.UserId);
        Assert.Equal("supervisor@example.test", actor.DisplayName);
        Assert.True(actor.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }

    [Fact]
    public async Task Http_adapter_reflects_HttpContext_User()
    {
        var actor = await Http(Principals.User("cw-1", "caseworker@example.test", AppRoles.CaseWorker)).GetAsync(CancellationToken.None);

        Assert.Equal("cw-1", actor.UserId);
        Assert.True(actor.HasPermission(Permissions.AttachmentsDownload));
        Assert.False(actor.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }

    [Fact]
    public async Task Anonymous_contexts_return_an_actor_without_permissions()
    {
        ICurrentUser[] adapters = [new CircuitCurrentUser(Circuit(Principals.Anonymous)), Http(Principals.Anonymous)];

        foreach (var adapter in adapters)
        {
            var actor = await adapter.GetAsync(CancellationToken.None);
            Assert.False(actor.IsAuthenticated);
            Assert.All(RolePermissionMap.AllPermissions, p => Assert.False(actor.HasPermission(p)));
        }
    }

    [Fact]
    public async Task Http_adapter_outside_a_request_fails_instead_of_guessing()
    {
        var adapter = new HttpCurrentUser(new HttpContextAccessor());

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Simultaneous_circuits_and_an_HTTP_download_each_observe_only_their_own_principal()
    {
        var first = new DeferredStateProvider();
        var second = new DeferredStateProvider();
        var pending = new[]
        {
            new CircuitCurrentUser(first).GetAsync(CancellationToken.None),
            new CircuitCurrentUser(second).GetAsync(CancellationToken.None),
            Http(Principals.User("http-user", "http@example.test", AppRoles.CaseWorker)).GetAsync(CancellationToken.None),
        };

        // Complete in the opposite order to the calls, so no ordering assumption can hide a crossed identity.
        second.State.SetResult(new AuthenticationState(Principals.User("circuit-2", "two@example.test", AppRoles.Supervisor)));
        first.State.SetResult(new AuthenticationState(Principals.User("circuit-1", "one@example.test", AppRoles.CaseWorker)));
        var actors = await Task.WhenAll(pending);

        Assert.Equal(["circuit-1", "circuit-2", "http-user"], actors.Select(a => a.UserId));
        Assert.False(actors[0].HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
        Assert.True(actors[1].HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
        Assert.Equal([AppRoles.CaseWorker], actors[2].Roles);
    }

    [Fact]
    public async Task A_revalidated_or_signed_out_circuit_denies_later_operations()
    {
        var provider = Circuit(Principals.User("sv-1", "sv", AppRoles.Supervisor));
        var adapter = new CircuitCurrentUser(provider);
        var before = await adapter.GetAsync(CancellationToken.None);

        // What the revalidating provider does when the security stamp no longer matches.
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(Principals.Anonymous)));
        var after = await adapter.GetAsync(CancellationToken.None);

        Assert.True(before.HasPermission(Permissions.CasesView));
        Assert.False(after.IsAuthenticated);
        Assert.False(after.HasPermission(Permissions.CasesView));
    }

    [Fact]
    public async Task Circuit_adapter_honors_cancellation_while_authentication_is_pending()
    {
        using var cts = new CancellationTokenSource();
        var pending = new CircuitCurrentUser(new DeferredStateProvider()).GetAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
