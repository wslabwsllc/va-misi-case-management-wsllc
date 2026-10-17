using System.Security.Claims;
using CaseIntake.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Account;

/// <summary>Routes of the account pages (static SSR, in <c>CaseIntake.Web/Components/Account</c>) and endpoints.</summary>
public static class AccountRoutes
{
    public const string Login = "/account/login";
    public const string Logout = "/account/logout";
    public const string AccessDenied = "/account/access-denied";
}

public static class AccountEndpoints
{
    /// <summary>
    /// Maps <c>POST /account/logout</c>. The form-bound parameter makes the endpoint require a valid antiforgery token
    /// (<c>UseAntiforgery</c>), so a cross-site form cannot sign a user out. Anonymous is allowed so an expired session
    /// can still "sign out" without a detour through the login page.
    /// </summary>
    public static IEndpointConventionBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapPost(AccountRoutes.Logout, async (
                ClaimsPrincipal user,
                SignInManager<AppUser> signInManager,
                ILoggerFactory loggerFactory,
                [FromForm] string? returnUrl) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                await signInManager.SignOutAsync();
                IdentityLog.LoggedOut(loggerFactory.CreateLogger(typeof(AccountEndpoints).FullName!), userId);
                return TypedResults.LocalRedirect(AccountRoutes.Login);
            })
            .AllowAnonymous()
            .WithName("AccountLogout");
    }
}
