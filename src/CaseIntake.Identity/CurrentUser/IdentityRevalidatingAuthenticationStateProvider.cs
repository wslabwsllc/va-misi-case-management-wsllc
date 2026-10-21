using System.Security.Claims;
using CaseIntake.Persistence;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.Identity.CurrentUser;

/// <summary>
/// Re-checks a connected circuit's user against the database every <see cref="IdentityModuleOptions.RevalidationInterval"/>
/// (Blazor template pattern). A deleted or deactivated user, or a changed security stamp (password change, role change, explicit
/// revocation) replaces the circuit's state with an anonymous user, so <see cref="CircuitCurrentUser"/> denies every
/// later operation. Each check uses its own DI scope: <see cref="UserManager{TUser}"/> and its context are never held
/// by the long-lived circuit.
/// </summary>
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> identityOptions,
    IOptions<IdentityModuleOptions> moduleOptions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<IdentityRevalidatingAuthenticationStateProvider>();

    protected override TimeSpan RevalidationInterval => moduleOptions.Value.RevalidationInterval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var valid = await ValidateSecurityStampAsync(userManager, authenticationState.User);
        if (!valid)
        {
            IdentityLog.CircuitAuthenticationRevoked(_logger, authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier));
        }
        return valid;
    }

    private async Task<bool> ValidateSecurityStampAsync(UserManager<AppUser> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive)
        {
            return false;
        }
        if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }

        var principalStamp = principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return principalStamp == userStamp;
    }
}
