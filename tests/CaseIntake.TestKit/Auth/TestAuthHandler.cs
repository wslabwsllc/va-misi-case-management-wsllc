using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.TestKit.Auth;

/// <summary>
/// Header-driven authentication for in-process tests: <c>X-Test-User</c> (id/name) and <c>X-Test-Roles</c>
/// (comma-separated). No header means anonymous. Test-only; never registered by the application.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ToString()),
            new(ClaimTypes.Name, user.ToString()),
        };
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => new Claim(ClaimTypes.Role, r)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Role presets for isolated authorization tests (E3). Browser end-to-end tests sign in with the real seeded accounts
/// instead; this handler is never part of the application.
/// </summary>
public static class TestUsers
{
    public const string CaseWorkerId = "test-caseworker";
    public const string SupervisorId = "test-supervisor";

    /// <summary>Authenticated, but with a role the permission matrix does not know, so it holds no permissions.</summary>
    public const string NoPermissionsId = "test-no-permissions";
    public const string NoPermissionsRole = "NoSuchRole";
}

public static class TestAuthExtensions
{
    public static HttpClient AsCaseWorker(this HttpClient client) =>
        client.WithUser(CaseIntake.Contracts.Security.AppRoles.CaseWorker, TestUsers.CaseWorkerId);

    public static HttpClient AsSupervisor(this HttpClient client) =>
        client.WithUser(CaseIntake.Contracts.Security.AppRoles.Supervisor, TestUsers.SupervisorId);

    public static HttpClient AsUserWithoutPermissions(this HttpClient client) =>
        client.WithUser(TestUsers.NoPermissionsRole, TestUsers.NoPermissionsId);

    /// <summary>Authenticates requests from this client as the given user and role (e.g. <c>AppRoles.Supervisor</c>).</summary>
    public static HttpClient WithUser(this HttpClient client, string role, string userId = "test-user")
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.RolesHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, role);
        return client;
    }
}
