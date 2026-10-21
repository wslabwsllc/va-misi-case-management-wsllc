using System.Collections.Immutable;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Authorization;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Administration;

/// <summary>
/// Role administration (ADR 0013). Permission changes take effect at the members' next check (the
/// <see cref="PermissionResolver"/> cache is cleared); renaming a custom role also updates its members' security stamps,
/// because their sessions carry the role name, so they sign in again within the revalidation interval. Built-in roles
/// keep their names and cannot be deleted; the Administrator role's permissions cannot be changed. Nobody adds or removes
/// a permission they do not hold, edits a role holding one, or removes their own ability to manage roles.
/// </summary>
internal sealed class RoleAdministrationService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    PermissionResolver resolver,
    TimeProvider time,
    ILogger<RoleAdministrationService> logger) : IRoleAdministrationService
{
    public async Task<Result<IReadOnlyList<RoleSummaryDto>>> ListAsync(CancellationToken ct)
    {
        if (await AuthorizeAsync("ListRoles", ct) is null)
        {
            return Result<IReadOnlyList<RoleSummaryDto>>.Failure(IdentityErrors.Forbidden);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var rolePermissions = await PermissionQueries.LoadRolePermissionsAsync(db, ct);
        var roles = await db.Roles.AsNoTracking().Where(r => r.Name != null)
            .Select(r => new { r.Id, Name = r.Name!, r.Description, r.IsBuiltIn, Members = db.UserRoles.Count(ur => ur.RoleId == r.Id) })
            .ToListAsync(ct);
        return Result<IReadOnlyList<RoleSummaryDto>>.Success([.. roles
            .OrderBy(r => r.IsBuiltIn ? RolePermissionMap.Roles.IndexOf(r.Name) : int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new RoleSummaryDto(r.Id, r.Name, r.Description, r.IsBuiltIn, IsLocked(r.Name), r.Members,
                Ordered(rolePermissions.GetValueOrDefault(r.Name) ?? [])))]);
    }

    public async Task<Result<RoleDetailDto>> GetAsync(string id, CancellationToken ct)
    {
        if (await AuthorizeAsync("GetRole", ct) is not { } actor)
        {
            return Result<RoleDetailDto>.Failure(IdentityErrors.Forbidden);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await DetailAsync(db, actor, id, ct) is { } detail
            ? Result<RoleDetailDto>.Success(detail)
            : Result<RoleDetailDto>.Failure(IdentityErrors.RoleNotFound);
    }

    public async Task<Result<RoleDetailDto>> CreateAsync(SaveRoleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AuthorizeAsync("CreateRole", ct) is not { } actor)
        {
            return Result<RoleDetailDto>.Failure(IdentityErrors.Forbidden);
        }

        using var operation = AuditOperation.BeginIfNone(logger, "CreateRole");
        var name = request.Name?.Trim() ?? string.Empty;
        var permissions = Distinct(request.Permissions);
        var roleId = await DatabaseUnit.RunAsync(contextFactory, scopeFactory, async (services, db, token) =>
        {
            var roles = services.GetRequiredService<RoleManager<AppRole>>();
            var errors = await ValidateNameAsync(roles, name, null);
            ValidateDescription(request.Description, errors);
            ValidatePermissions(actor, [], permissions, errors);
            if (errors.Count > 0)
            {
                return (Result<string>.Failure(IdentityErrors.Validation(errors)), false);
            }

            var role = new AppRole { Name = name, Description = Clean(request.Description) };
            Ensure(await roles.CreateAsync(role), "create the role");
            db.RolePermissions.AddRange(permissions.Select(p => new AppRolePermission { RoleId = role.Id, PermissionName = p }));
            await db.SaveChangesAsync(token);
            await AuditAsync(db, actor, AuditLogActions.RoleCreated, role, "Role created", null, Snapshot(name, role.Description, permissions));
            return (Result<string>.Success(role.Id), true);
        }, ct);
        if (!roleId.IsSuccess)
        {
            return Result<RoleDetailDto>.Failure(roleId.Error!);
        }

        resolver.Invalidate();
        IdentityLog.RoleCreated(logger, actor.UserId, roleId.Value!, name, string.Join(",", Ordered(permissions)));
        return await FreshDetailAsync(actor, roleId.Value!, ct);
    }

    public async Task<Result<RoleDetailDto>> UpdateAsync(string id, SaveRoleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AuthorizeAsync("UpdateRole", ct) is not { } actor)
        {
            return Result<RoleDetailDto>.Failure(IdentityErrors.Forbidden);
        }

        using var operation = AuditOperation.BeginIfNone(logger, "UpdateRole");
        var outcome = await DatabaseUnit.RunAsync(contextFactory, scopeFactory, async (services, db, token) =>
        {
            var roles = services.GetRequiredService<RoleManager<AppRole>>();
            var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, token);
            if (role?.Name is null)
            {
                return (new UpdateOutcome(IdentityErrors.RoleNotFound), false);
            }
            if (!string.Equals(role.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
            {
                return (new UpdateOutcome(IdentityErrors.Conflict), false);
            }

            var rolePermissions = await PermissionQueries.LoadRolePermissionsAsync(db, token);
            var before = rolePermissions.GetValueOrDefault(role.Name) ?? [];
            if (CannotManage(actor, role.Name, before) is { } reason)
            {
                IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.RolesManage, role.Id);
                return (new UpdateOutcome(IdentityErrors.NotAllowed(reason)), false);
            }

            var name = role.IsBuiltIn ? role.Name : request.Name?.Trim() ?? string.Empty;
            var permissions = Distinct(request.Permissions);
            var errors = await ValidateNameAsync(roles, name, role.Id);
            ValidateDescription(request.Description, errors);
            ValidatePermissions(actor, before, permissions, errors);
            if (errors.Count == 0 && await WouldLoseRoleManagementAsync(db, actor, role.Name, permissions, rolePermissions, token))
            {
                errors[RoleFieldNames.Permissions] =
                    ["You hold this role, and without Manage roles here you would lose the ability to manage roles. Keep Manage roles, or ask another administrator."];
            }
            if (errors.Count > 0)
            {
                return (new UpdateOutcome(IdentityErrors.Validation(errors)), false);
            }

            var snapshotBefore = Snapshot(role.Name, role.Description, before);
            // The stored rows become exactly the requested set (an unseeded built-in role had its defaults implied, not stored).
            db.RolePermissions.RemoveRange(role.Permissions.Where(p => !permissions.Contains(p.PermissionName)));
            foreach (var permission in permissions.Where(p => role.Permissions.All(s => s.PermissionName != p)))
            {
                role.Permissions.Add(new AppRolePermission { RoleId = role.Id, PermissionName = permission });
            }
            if (role.IsBuiltIn && role.DefaultPermissionsApplied.Count == 0)
            {
                // The defaults were implied, not stored: record them as applied so the seed never grants them again.
                role.DefaultPermissionsApplied = [.. RolePermissionMap.PermissionsFor([role.Name!])];
            }
            role.Description = Clean(request.Description);
            var renamed = !string.Equals(name, role.Name, StringComparison.Ordinal);
            if (renamed)
            {
                Ensure(await roles.SetRoleNameAsync(role, name), "rename the role");
            }
            var saved = await roles.UpdateAsync(role);
            if (IdentityErrors.IsConcurrencyFailure(saved))
            {
                return (new UpdateOutcome(IdentityErrors.Conflict), false);
            }
            Ensure(saved, "save the role");
            var snapshotAfter = Snapshot(name, role.Description, permissions);
            var changedFields = snapshotAfter.Keys.Where(k => snapshotBefore[k] != snapshotAfter[k]).ToList();
            await AuditAsync(db, actor, AuditLogActions.RoleUpdated, role,
                changedFields.Count == 0 ? "Saved without changes" : $"Changed: {string.Join(", ", changedFields)}",
                changedFields.ToDictionary(k => k, k => snapshotBefore[k]), changedFields.ToDictionary(k => k, k => snapshotAfter[k]));
            if (renamed)
            {
                await SignOutMembersAsync(services, db, role.Id, token);
            }
            return (new UpdateOutcome(null, name,
                permissions.Except(before, StringComparer.Ordinal).ToList(), before.Except(permissions, StringComparer.Ordinal).ToList()), true);
        }, ct);
        if (outcome.Error is not null)
        {
            return Result<RoleDetailDto>.Failure(outcome.Error);
        }

        resolver.Invalidate();
        IdentityLog.RoleUpdated(logger, actor.UserId, id, outcome.Name, string.Join(",", Ordered(outcome.Added)), string.Join(",", Ordered(outcome.Removed)));
        return await FreshDetailAsync(actor, id, ct);
    }

    private sealed record UpdateOutcome(Error? Error, string Name = "", IReadOnlyList<string>? AddedOrNull = null, IReadOnlyList<string>? RemovedOrNull = null)
    {
        public IReadOnlyList<string> Added => AddedOrNull ?? [];
        public IReadOnlyList<string> Removed => RemovedOrNull ?? [];
    }

    public async Task<Result<string>> DeleteAsync(string id, string concurrencyStamp, CancellationToken ct)
    {
        if (await AuthorizeAsync("DeleteRole", ct) is not { } actor)
        {
            return Result<string>.Failure(IdentityErrors.Forbidden);
        }

        using var operation = AuditOperation.BeginIfNone(logger, "DeleteRole");
        var outcome = await DatabaseUnit.RunAsync(contextFactory, scopeFactory, async (services, db, token) =>
        {
            var roles = services.GetRequiredService<RoleManager<AppRole>>();
            var role = await roles.FindByIdAsync(id);
            if (role?.Name is null)
            {
                return (Result<string>.Failure(IdentityErrors.RoleNotFound), false);
            }
            if (!string.Equals(role.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal))
            {
                return (Result<string>.Failure(IdentityErrors.Conflict), false);
            }
            if (role.IsBuiltIn)
            {
                return (Result<string>.Failure(IdentityErrors.NotAllowed("Built-in roles cannot be deleted.")), false);
            }
            var permissions = (await PermissionQueries.LoadRolePermissionsAsync(db, token)).GetValueOrDefault(role.Name) ?? [];
            if (CannotManage(actor, role.Name, permissions) is { } reason)
            {
                IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.RolesManage, role.Id);
                return (Result<string>.Failure(IdentityErrors.NotAllowed(reason)), false);
            }
            if (await db.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, token))
            {
                return (Result<string>.Failure(IdentityErrors.NotAllowed("Remove everyone from this role before deleting it.")), false);
            }

            var snapshot = Snapshot(role.Name, role.Description, permissions);
            var deleted = await roles.DeleteAsync(role);
            if (IdentityErrors.IsConcurrencyFailure(deleted))
            {
                return (Result<string>.Failure(IdentityErrors.Conflict), false);
            }
            Ensure(deleted, "delete the role");
            await AuditAsync(db, actor, AuditLogActions.RoleDeleted, role, "Role deleted", snapshot, null);
            return (Result<string>.Success(role.Name), true);
        }, ct);
        if (outcome.IsSuccess)
        {
            resolver.Invalidate();
            IdentityLog.RoleDeleted(logger, actor.UserId, id, outcome.Value!);
        }
        return outcome;
    }

    private async Task<OperationActor?> AuthorizeAsync(string operation, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (actor.HasPermission(Permissions.RolesManage))
        {
            return actor;
        }
        IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.RolesManage, operation);
        return null;
    }

    private async Task<Result<RoleDetailDto>> FreshDetailAsync(OperationActor actor, string id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await DetailAsync(db, actor, id, ct) is { } detail
            ? Result<RoleDetailDto>.Success(detail)
            : Result<RoleDetailDto>.Failure(IdentityErrors.RoleNotFound);
    }

    private static async Task<RoleDetailDto?> DetailAsync(AppDbContext db, OperationActor actor, string id, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (role?.Name is null)
        {
            return null;
        }

        var rolePermissions = await PermissionQueries.LoadRolePermissionsAsync(db, ct);
        var permissions = rolePermissions.GetValueOrDefault(role.Name) ?? [];
        var members = await (
            from userRole in db.UserRoles
            join user in db.Users on userRole.UserId equals user.Id
            where userRole.RoleId == id && user.UserName != null
            orderby user.NormalizedUserName
            select user.UserName!).ToListAsync(ct);
        var reason = CannotManage(actor, role.Name, permissions);
        return new RoleDetailDto(role.Id, role.Name, role.Description, role.IsBuiltIn, IsLocked(role.Name), Ordered(permissions),
            members, role.ConcurrencyStamp ?? string.Empty, reason is null, reason);
    }

    private static bool IsLocked(string roleName) => roleName == AppRoles.Administrator;

    /// <summary>The audited fields of a role: name, description and its permissions in catalog order.</summary>
    private static Dictionary<string, string?> Snapshot(string name, string? description, IEnumerable<string> permissions) => new()
    {
        ["Name"] = name,
        ["Description"] = description,
        ["Permissions"] = string.Join(", ", Ordered(permissions)),
    };

    /// <summary>Records the change in the system audit log on the transaction's context and saves it (ADR 0014).</summary>
    private async Task AuditAsync(AppDbContext db, OperationActor actor, string action, AppRole role, string summary,
        IReadOnlyDictionary<string, string?>? before, IReadOnlyDictionary<string, string?>? after)
    {
        SystemAudit.Append(db, new AuditRecord(AuditCategories.Roles, action, AuditOutcome.Success, actor.UserId,
            AuditAppender.ActorName(actor), AuditEntityTypes.Role, role.Id, role.Name, summary, before, after), time.GetUtcNow());
        await db.SaveChangesAsync();
    }

    private static string? CannotManage(OperationActor actor, string roleName, IEnumerable<string> permissions)
    {
        if (IsLocked(roleName))
        {
            return "The Administrator role always has every permission and cannot be changed.";
        }
        var missing = AdministrationRules.Missing(actor, permissions);
        return missing.Count == 0
            ? null
            : $"This role has permissions you do not have ({AdministrationRules.Labels(missing)}), so only someone with at least the same access can change it.";
    }

    /// <summary>True when the actor is a member and would no longer hold <see cref="Permissions.RolesManage"/> after the change.</summary>
    private static async Task<bool> WouldLoseRoleManagementAsync(AppDbContext db, OperationActor actor, string roleName,
        IReadOnlyCollection<string> permissions, IReadOnlyDictionary<string, ImmutableHashSet<string>> rolePermissions, CancellationToken ct)
    {
        if (permissions.Contains(Permissions.RolesManage) || actor.UserId is null
            || await PermissionQueries.LoadUserAccessAsync(db, actor.UserId, ct) is not { } access
            || !access.Roles.Contains(roleName))
        {
            return false;
        }
        var after = new Dictionary<string, ImmutableHashSet<string>>(rolePermissions, StringComparer.Ordinal)
        {
            [roleName] = [.. permissions],
        };
        var fromRoles = access.Roles.SelectMany(r => after.TryGetValue(r, out var granted) ? granted : []);
        return !EffectivePermissions.Compute(access.IsActive, fromRoles, access.Overrides).Contains(Permissions.RolesManage);
    }

    private static async Task SignOutMembersAsync(IServiceProvider services, AppDbContext db, string roleId, CancellationToken ct)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var memberIds = await db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(ct);
        foreach (var memberId in memberIds)
        {
            if (await users.FindByIdAsync(memberId) is { } member)
            {
                Ensure(await users.UpdateSecurityStampAsync(member), "sign out the role's members");
            }
        }
    }

    private static async Task<Dictionary<string, string[]>> ValidateNameAsync(RoleManager<AppRole> roles, string name, string? roleId)
    {
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0)
        {
            errors[RoleFieldNames.Name] = ["Enter a role name."];
        }
        else if (name.Length > RoleFieldNames.NameMaxLength)
        {
            errors[RoleFieldNames.Name] = [$"Use at most {RoleFieldNames.NameMaxLength} characters."];
        }
        else if (name.Contains(',', StringComparison.Ordinal) || name.Any(char.IsControl))
        {
            errors[RoleFieldNames.Name] = ["Use letters, numbers, spaces and simple punctuation (no commas)."];
        }
        else if (await roles.FindByNameAsync(name) is { } existing && existing.Id != roleId)
        {
            errors[RoleFieldNames.Name] = ["A role with this name already exists."];
        }
        return errors;
    }

    private static void ValidateDescription(string? description, Dictionary<string, string[]> errors)
    {
        if (Clean(description) is { Length: > RoleFieldNames.DescriptionMaxLength })
        {
            errors[RoleFieldNames.Description] = [$"Use at most {RoleFieldNames.DescriptionMaxLength} characters."];
        }
    }

    private static void ValidatePermissions(OperationActor actor, IReadOnlyCollection<string> before,
        IReadOnlyCollection<string> after, Dictionary<string, string[]> errors)
    {
        if (after.Any(p => !PermissionCatalog.Contains(p)))
        {
            errors[RoleFieldNames.Permissions] = ["Choose permissions from the list."];
            return;
        }
        var changed = after.Except(before, StringComparer.Ordinal).Concat(before.Except(after, StringComparer.Ordinal));
        if (AdministrationRules.Missing(actor, changed) is { Count: > 0 } missing)
        {
            errors[RoleFieldNames.Permissions] =
                [$"You can only add or remove permissions you hold yourself. You do not hold: {AdministrationRules.Labels(missing)}."];
        }
    }

    private static string? Clean(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static List<string> Distinct(IReadOnlyList<string>? permissions) =>
        [.. (permissions ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal)];

    /// <summary>Catalog order, unknown names last.</summary>
    private static List<string> Ordered(IEnumerable<string> permissions)
    {
        var set = permissions.ToHashSet(StringComparer.Ordinal);
        return [.. RolePermissionMap.AllPermissions.Where(set.Contains), .. set.Where(p => !PermissionCatalog.Contains(p)).Order(StringComparer.Ordinal)];
    }

    private static void Ensure(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Failed to {operation}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }
}
