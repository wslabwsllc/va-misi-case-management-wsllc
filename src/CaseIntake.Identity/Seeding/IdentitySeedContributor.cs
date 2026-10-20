using CaseIntake.Contracts.Security;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Seeding;

/// <summary>A synthetic demo account. The <c>.test</c> TLD is reserved (RFC 2606) and never routes anywhere real.</summary>
public sealed record DemoUser(string Id, string UserName, string Role);

public static class DemoUsers
{
    /// <summary>The demo password setting; demo users exist only while it is configured.</summary>
    public const string PasswordKey = "Seed:DemoPassword";

    public static DemoUser CaseWorker { get; } = new("6f1d1c52-0e3a-4c1b-9a51-000000000001", "caseworker@example.test", AppRoles.CaseWorker);
    public static DemoUser Supervisor { get; } = new("6f1d1c52-0e3a-4c1b-9a51-000000000002", "supervisor@example.test", AppRoles.Supervisor);
    public static DemoUser QualityAnalyst { get; } = new("6f1d1c52-0e3a-4c1b-9a51-000000000003", "qa@example.test", AppRoles.QualityAnalyst);
    public static DemoUser IntakeClerk { get; } = new("6f1d1c52-0e3a-4c1b-9a51-000000000004", "intake@example.test", AppRoles.IntakeClerk);
    public static DemoUser Auditor { get; } = new("6f1d1c52-0e3a-4c1b-9a51-000000000005", "auditor@example.test", AppRoles.Auditor);
    public static IReadOnlyList<DemoUser> All { get; } = [Auditor, IntakeClerk, CaseWorker, Supervisor, QualityAnalyst];

    /// <summary>Stable role IDs, so two fresh databases seed identically.</summary>
    internal static string RoleId(string role) => $"role-{role.ToLowerInvariant()}";
}

/// <summary>
/// Seeds every role and one demo user per role (E3-S4) through <see cref="RoleManager{TRole}"/> and
/// <see cref="UserManager{TUser}"/>, which save through their own store context. Idempotent: existing roles and users
/// are kept as they are (passwords are never reset), and only missing roles, users or memberships are added, so a
/// partially completed run is completed. Any failed Identity result throws, failing the migrator.
/// </summary>
internal sealed class IdentitySeedContributor(
    RoleManager<AppRole> roleManager,
    UserManager<AppUser> userManager,
    IConfiguration configuration,
    ILogger<IdentitySeedContributor> logger) : ISeedContributor
{
    public const string DemoPasswordKey = DemoUsers.PasswordKey;

    public static int Order => 10;

    public async Task SeedAsync(CancellationToken ct)
    {
        var password = configuration[DemoPasswordKey];
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"'{DemoPasswordKey}' is not configured. Set Seed__DemoPassword.");
        }

        var rolesCreated = 0;
        foreach (var role in RolePermissionMap.Roles)
        {
            ct.ThrowIfCancellationRequested();
            if (!await roleManager.RoleExistsAsync(role))
            {
                Ensure(await roleManager.CreateAsync(new AppRole { Id = DemoUsers.RoleId(role), Name = role }), $"create role {role}");
                rolesCreated++;
            }
        }

        var usersCreated = 0;
        var membershipsAdded = 0;
        foreach (var demo in DemoUsers.All)
        {
            ct.ThrowIfCancellationRequested();
            var user = await userManager.FindByNameAsync(demo.UserName);
            if (user is null)
            {
                user = new AppUser
                {
                    Id = demo.Id,
                    UserName = demo.UserName,
                    Email = demo.UserName,
                    EmailConfirmed = true,
                    LockoutEnabled = true,
                };
                Ensure(await userManager.CreateAsync(user, password), $"create user {demo.UserName}");
                usersCreated++;
            }
            else if (!await userManager.GetLockoutEnabledAsync(user))
            {
                Ensure(await userManager.SetLockoutEnabledAsync(user, true), $"enable lockout for {demo.UserName}");
            }

            if (!await userManager.IsInRoleAsync(user, demo.Role))
            {
                Ensure(await userManager.AddToRoleAsync(user, demo.Role), $"add {demo.UserName} to {demo.Role}");
                membershipsAdded++;
            }
        }

        IdentityLog.IdentitySeeded(logger, rolesCreated, usersCreated, membershipsAdded);
    }

    private static void Ensure(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            // Error codes only: descriptions can echo submitted values.
            throw new InvalidOperationException(
                $"Identity seed failed to {operation}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }
}
