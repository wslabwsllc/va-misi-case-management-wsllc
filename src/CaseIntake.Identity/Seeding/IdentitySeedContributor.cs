using CaseIntake.Contracts.Security;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    public static DemoUser Administrator { get; } = new("6f1d1c52-0e3a-4c1b-9a51-000000000006", "admin@example.test", AppRoles.Administrator);
    public static IReadOnlyList<DemoUser> All { get; } = [Auditor, IntakeClerk, CaseWorker, Supervisor, QualityAnalyst, Administrator];

    /// <summary>Stable role IDs, so two fresh databases seed identically.</summary>
    internal static string RoleId(string role) => $"role-{role.ToLowerInvariant()}";
}

/// <summary>
/// Seeds the permission catalog, every built-in role with its default permissions, and one demo user per role (E3-S4,
/// ADR 0013). Roles and users go through <see cref="RoleManager{TRole}"/> and <see cref="UserManager{TUser}"/>; the
/// catalog and grants through a context of their own. Idempotent: the catalog follows <see cref="PermissionCatalog"/>
/// (a removed permission takes its grants with it); a built-in role's defaults are written once and never again, so an
/// administrator's changes survive; the Administrator role is re-granted every permission on every run; existing users
/// keep their passwords and only missing users or memberships are added. Any failed Identity result throws, failing
/// the migrator.
/// </summary>
internal sealed class IdentitySeedContributor(
    RoleManager<AppRole> roleManager,
    UserManager<AppUser> userManager,
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider time,
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

        await SyncCatalogAsync(ct);

        var rolesCreated = 0;
        foreach (var role in RolePermissionMap.Roles)
        {
            ct.ThrowIfCancellationRequested();
            var existing = await roleManager.FindByNameAsync(role);
            if (existing is null)
            {
                var created = new AppRole { Id = DemoUsers.RoleId(role), Name = role, Description = DefaultDescription(role), IsBuiltIn = true };
                Ensure(await roleManager.CreateAsync(created), $"create role {role}");
                rolesCreated++;
            }
            else if (!existing.IsBuiltIn)
            {
                existing.IsBuiltIn = true;
                existing.Description ??= DefaultDescription(role);
                Ensure(await roleManager.UpdateAsync(existing), $"mark role {role} as built in");
            }
        }

        var grantsAdded = await SeedRolePermissionsAsync(ct);

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
                    CreatedUtc = time.GetUtcNow(),
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

        IdentityLog.IdentitySeeded(logger, rolesCreated, usersCreated, membershipsAdded, grantsAdded);
    }

    /// <summary>Upserts every catalog permission and deletes rows the code no longer defines.</summary>
    private async Task SyncCatalogAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var stored = await db.Permissions.ToDictionaryAsync(p => p.Name, StringComparer.Ordinal, ct);
        foreach (var definition in PermissionCatalog.All)
        {
            if (stored.Remove(definition.Name, out var row))
            {
                row.Group = definition.Group;
                row.Label = definition.Label;
                row.Description = definition.Description;
            }
            else
            {
                db.Permissions.Add(new AppPermission
                {
                    Name = definition.Name, Group = definition.Group, Label = definition.Label, Description = definition.Description,
                });
            }
        }
        db.Permissions.RemoveRange(stored.Values);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Grants each built-in role the default permissions it has never been given (all of them on a first run, only new
    /// defaults afterwards), and every permission to the Administrator role. A default an administrator removed stays
    /// removed because it is already in <see cref="AppRole.DefaultPermissionsApplied"/>. Returns the grants added.
    /// </summary>
    private async Task<int> SeedRolePermissionsAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var builtIn = await db.Roles.Include(r => r.Permissions)
            .Where(r => r.IsBuiltIn && r.Name != null)
            .ToListAsync(ct);

        var added = 0;
        foreach (var role in builtIn)
        {
            var defaults = RolePermissionMap.PermissionsFor([role.Name!]);
            var applied = role.DefaultPermissionsApplied.ToHashSet(StringComparer.Ordinal);
            IEnumerable<string> wanted = role.Name == AppRoles.Administrator
                ? RolePermissionMap.AllPermissions
                : RolePermissionMap.AllPermissions.Where(p => defaults.Contains(p) && !applied.Contains(p));
            var have = role.Permissions.Select(p => p.PermissionName).ToHashSet(StringComparer.Ordinal);
            foreach (var permission in wanted.Where(have.Add))
            {
                role.Permissions.Add(new AppRolePermission { RoleId = role.Id, PermissionName = permission });
                added++;
            }
            if (!defaults.IsSubsetOf(applied))
            {
                role.DefaultPermissionsApplied = [.. RolePermissionMap.AllPermissions.Where(p => applied.Contains(p) || defaults.Contains(p))];
            }
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    private static string DefaultDescription(string role) => role switch
    {
        AppRoles.Auditor => "Read-only review of cases, audit trails and documents.",
        AppRoles.IntakeClerk => "Opens new cases, looks up veterans and attaches documents.",
        AppRoles.CaseWorker => "Edits cases, starts and resolves them.",
        AppRoles.Supervisor => "Case work plus triage, assignment, closing and reopening cases, and managing users.",
        AppRoles.QualityAnalyst => "Reads cases; views and imports test results.",
        AppRoles.Administrator => "Every permission, including managing users and roles. Cannot be changed.",
        _ => role,
    };

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
