using CaseIntake.Contracts.Security;
using Microsoft.AspNetCore.Components.Authorization;

namespace CaseIntake.Identity.CurrentUser;

/// <summary>
/// <see cref="ICurrentUser"/> for interactive components and static SSR pages: reads the circuit's (or prerender's)
/// principal from <see cref="AuthenticationStateProvider"/> on every call, so a revalidation or sign-out that replaces
/// the authentication state is seen by the next operation. Never reads <c>HttpContext</c>, which is absent or stale
/// inside a circuit.
/// </summary>
public sealed class CircuitCurrentUser(AuthenticationStateProvider authenticationStateProvider) : ICurrentUser
{
    public async Task<OperationActor> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state = await authenticationStateProvider.GetAuthenticationStateAsync().WaitAsync(ct);
        return OperationActorFactory.FromPrincipal(state.User);
    }
}
