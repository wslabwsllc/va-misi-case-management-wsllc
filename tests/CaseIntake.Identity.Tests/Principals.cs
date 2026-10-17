using System.Security.Claims;

namespace CaseIntake.Identity.Tests;

internal static class Principals
{
    public static ClaimsPrincipal Anonymous { get; } = new(new ClaimsIdentity());

    public static ClaimsPrincipal User(string id, string name, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, name) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
