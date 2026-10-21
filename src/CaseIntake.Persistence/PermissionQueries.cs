using System.Collections.Immutable;
using CaseIntake.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.Persistence;

/// <summary>A user's stored access: active flag, role names and overrides. <c>null</c> from the query when the user does not exist.</summary>
public sealed record StoredUserAccess(string UserId, bool IsActive, ImmutableArray<string> Roles,
    IReadOnlyDictionary<string, PermissionEffect> Overrides);

/// <summary>
/// The database side of the permission rule (ADR 0013), shared by the Identity resolver and modules that ask "who holds
/// this permission" (case assignees) without referencing each other.
/// </summary>
public static class PermissionQueries
{
    /// <summary>
    /// Every role's permissions by role name. A built-in role (by name) whose default permissions the seed has not
    /// written yet, including one with no row at all as in an unseeded test database, grants its
    /// <see cref="RolePermissionMap"/> defaults, so a migrated but unseeded database behaves like the compiled matrix.
    /// Unknown roles are absent and grant nothing.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, ImmutableHashSet<string>>> LoadRolePermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var roles = await db.Roles.AsNoTracking()
            .Where(r => r.Name != null)
            .Select(r => new { Name = r.Name!, Seeded = r.DefaultPermissionsApplied.Count > 0, Permissions = r.Permissions.Select(p => p.PermissionName).ToList() })
            .ToListAsync(ct);

        var result = new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            result[role.Name] = RolePermissionMap.Roles.Contains(role.Name) && !role.Seeded
                ? RolePermissionMap.PermissionsFor([role.Name])
                : role.Permissions.ToImmutableHashSet(StringComparer.Ordinal);
        }
        foreach (var builtIn in RolePermissionMap.Roles)
        {
            result.TryAdd(builtIn, RolePermissionMap.PermissionsFor([builtIn]));
        }
        return result;
    }

    public static async Task<StoredUserAccess?> LoadUserAccessAsync(AppDbContext db, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.IsActive,
                Overrides = u.PermissionOverrides.Select(o => new { o.PermissionName, o.Effect }).ToList(),
            })
            .SingleOrDefaultAsync(ct);
        if (user is null)
        {
            return null;
        }

        var roles = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.Name != null
            select role.Name!).ToListAsync(ct);

        return new StoredUserAccess(userId, user.IsActive, [.. roles.Order(StringComparer.Ordinal)],
            user.Overrides.ToDictionary(o => o.PermissionName, o => o.Effect, StringComparer.Ordinal));
    }

    /// <summary>
    /// The IDs of active users who hold <paramref name="permission"/> through their stored roles and overrides (a denial
    /// wins). Evaluated in memory over the role map, so built-in defaults apply exactly as in
    /// <see cref="LoadRolePermissionsAsync"/>.
    /// </summary>
    /// <param name="onlyUserId">When set, checks just that user (an empty or one-element result).</param>
    public static async Task<IReadOnlyList<string>> ActiveUserIdsHoldingAsync(AppDbContext db, string permission, CancellationToken ct,
        string? onlyUserId = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rolePermissions = await LoadRolePermissionsAsync(db, ct);
        var grantingRoles = rolePermissions.Where(r => r.Value.Contains(permission)).Select(r => r.Key).ToList();

        var users = db.Users.AsNoTracking();
        if (onlyUserId is not null)
        {
            users = users.Where(u => u.Id == onlyUserId);
        }
        return await users
            .Where(u => u.IsActive
                && !u.PermissionOverrides.Any(o => o.PermissionName == permission && o.Effect == PermissionEffect.Deny)
                && (u.PermissionOverrides.Any(o => o.PermissionName == permission && o.Effect == PermissionEffect.Allow)
                    || (from userRole in db.UserRoles
                        join role in db.Roles on userRole.RoleId equals role.Id
                        where userRole.UserId == u.Id && grantingRoles.Contains(role.Name!)
                        select userRole.UserId).Any()))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }
}
