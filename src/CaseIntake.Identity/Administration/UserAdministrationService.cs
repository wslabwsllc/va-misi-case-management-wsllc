using System.Collections.Immutable;
using System.Net.Mail;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Cases;
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
/// User administration (ADR 0013). Runs in the circuit, so <see cref="UserManager{TUser}"/> is resolved in a fresh scope
/// per call and never held by the circuit (ADR 0007). Every change runs in one transaction on that scope's context,
/// starts from the caller's concurrency stamp, follows <see cref="AdministrationRules"/> and clears the
/// <see cref="PermissionResolver"/> cache. Changing the email or roles, or deactivating, updates the security stamp, so
/// the user's sessions end within the revalidation interval. Users are deactivated, never deleted (their IDs appear in
/// case history).
/// </summary>
internal sealed class UserAdministrationService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    PermissionResolver resolver,
    TimeProvider time,
    ILogger<UserAdministrationService> logger) : IUserAdministrationService
{
    /// <summary>Identity's default user name and email column length.</summary>
    internal const int EmailMaxLength = 256;

    public async Task<Result<PagedResult<UserSummaryDto>>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await AuthorizeAsync("ListUsers", ct) is not { } actor)
        {
            return Result<PagedResult<UserSummaryDto>>.Failure(IdentityErrors.Forbidden);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, UserListQuery.MaxPageSize);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var users = db.Users.AsNoTracking().Where(u => u.UserName != null);
        if (query.Search?.Trim() is { Length: > 0 } search)
        {
            var normalized = search.ToUpperInvariant();
            users = users.Where(u => u.NormalizedUserName!.Contains(normalized));
        }
        if (query.Role is { Length: > 0 } roleName)
        {
            users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == roleName)));
        }
        users = query.Status switch
        {
            UserStatusFilter.Active => users.Where(u => u.IsActive),
            UserStatusFilter.Inactive => users.Where(u => !u.IsActive),
            _ => users,
        };

        var total = await users.CountAsync(ct);
        var rows = await users.OrderBy(u => u.NormalizedUserName).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, UserName = u.UserName!, u.IsActive, u.LockoutEnd, u.LastSignInUtc })
            .ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var roles = (await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where ids.Contains(userRole.UserId) && role.Name != null
            select new { userRole.UserId, Name = role.Name! }).ToListAsync(ct))
            .ToLookup(r => r.UserId, r => r.Name);

        var now = time.GetUtcNow();
        return Result<PagedResult<UserSummaryDto>>.Success(new PagedResult<UserSummaryDto>([.. rows.Select(u =>
            new UserSummaryDto(u.Id, u.UserName, [.. roles[u.Id].Order(StringComparer.Ordinal)], u.IsActive,
                u.LockoutEnd > now, u.LastSignInUtc))], page, pageSize, total));
    }

    public async Task<Result<IReadOnlyList<RoleOptionDto>>> ListRoleOptionsAsync(CancellationToken ct)
    {
        if (await AuthorizeAsync("ListRoleOptions", ct) is not { } actor)
        {
            return Result<IReadOnlyList<RoleOptionDto>>.Failure(IdentityErrors.Forbidden);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return Result<IReadOnlyList<RoleOptionDto>>.Success(await RoleOptionsAsync(db, actor, ct));
    }

    public async Task<Result<UserSummaryDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AuthorizeAsync("CreateUser", ct) is not { } actor)
        {
            return Result<UserSummaryDto>.Failure(IdentityErrors.Forbidden);
        }

        var email = request.Email?.Trim() ?? string.Empty;
        var roles = Distinct(request.Roles);
        await using (var db = await contextFactory.CreateDbContextAsync(ct))
        {
            var errors = ValidateEmail(email);
            if (await ValidateRolesAsync(db, actor, [], roles, ct) is { } roleError)
            {
                errors[UserFieldNames.Roles] = [roleError];
            }
            if (string.IsNullOrEmpty(request.Password))
            {
                errors[UserFieldNames.Password] = ["Enter an initial password."];
            }
            if (errors.Count > 0)
            {
                return Result<UserSummaryDto>.Failure(IdentityErrors.Validation(errors));
            }
        }

        using var operation = AuditOperation.BeginIfNone(logger, "CreateUser");
        var outcome = await DatabaseUnit.RunAsync(contextFactory, scopeFactory, async (services, db, _) =>
        {
            var users = services.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                UserName = email, Email = email, EmailConfirmed = true, LockoutEnabled = true, CreatedUtc = time.GetUtcNow(),
            };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return (Result<UserSummaryDto>.Failure(IdentityErrors.FromCreate(created)), false);
            }
            Ensure(await users.AddToRolesAsync(user, roles), "add the new user to their roles");
            await AuditAsync(db, actor, user, AuditLogActions.UserCreated, $"Added with roles: {RoleList(roles)}",
                null, new Dictionary<string, string?> { ["Email"] = email, ["Roles"] = RoleList(roles), ["Active"] = "Yes" });
            return (Result<UserSummaryDto>.Success(new UserSummaryDto(user.Id, email, [.. roles.Order(StringComparer.Ordinal)])), true);
        }, ct);
        if (outcome.IsSuccess)
        {
            resolver.Invalidate();
            IdentityLog.UserCreated(logger, actor.UserId, outcome.Value!.Id, string.Join(",", roles));
        }
        return outcome;
    }

    public async Task<Result<UserDetailDto>> GetAsync(string id, CancellationToken ct)
    {
        if (await AuthorizeAsync("GetUser", ct) is not { } actor)
        {
            return Result<UserDetailDto>.Failure(IdentityErrors.Forbidden);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await DetailAsync(db, actor, id, ct) is { } detail
            ? Result<UserDetailDto>.Success(detail)
            : Result<UserDetailDto>.Failure(IdentityErrors.UserNotFound);
    }

    public async Task<Result<UserDetailDto>> UpdateAsync(string id, UpdateUserRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ChangeAsync(id, request.ConcurrencyStamp, "UpdateUser", ct, async (change, actor, user, current) =>
        {
            var email = request.Email?.Trim() ?? string.Empty;
            var roles = Distinct(request.Roles);
            var errors = ValidateEmail(email);
            if (await ValidateRolesAsync(change.Db, actor, current.Roles, roles, ct) is { } roleError)
            {
                errors[UserFieldNames.Roles] = [roleError];
            }
            if (errors.Count > 0)
            {
                return IdentityErrors.Validation(errors);
            }

            var previousEmail = user.UserName;
            var changes = new List<string>();
            if (!string.Equals(email, user.UserName, StringComparison.Ordinal))
            {
                user.Email = email;
                var renamed = await change.Users.SetUserNameAsync(user, email);
                if (!renamed.Succeeded)
                {
                    return IdentityErrors.IsConcurrencyFailure(renamed) ? IdentityErrors.Conflict : IdentityErrors.FromCreate(renamed);
                }
                changes.Add("Email");
            }

            var removed = current.Roles.Except(roles, StringComparer.Ordinal).ToList();
            var added = roles.Except(current.Roles, StringComparer.Ordinal).ToList();
            if (removed.Count > 0 || added.Count > 0)
            {
                if (removed.Count > 0)
                {
                    Ensure(await change.Users.RemoveFromRolesAsync(user, removed), "remove roles");
                }
                if (added.Count > 0)
                {
                    Ensure(await change.Users.AddToRolesAsync(user, added), "add roles");
                }
                changes.Add("Roles");
            }
            if (changes.Count == 0)
            {
                // Nothing to save, but the stamp still proves the form was current.
                return await change.TouchAsync(user);
            }

            var before = new Dictionary<string, string?>();
            var after = new Dictionary<string, string?>();
            if (changes.Contains("Email"))
            {
                (before["Email"], after["Email"]) = (previousEmail, email);
            }
            if (changes.Contains("Roles"))
            {
                (before["Roles"], after["Roles"]) = (RoleList(current.Roles), RoleList(roles));
            }
            await AuditAsync(change.Db, actor, user, AuditLogActions.UserUpdated, $"Changed: {string.Join(", ", changes)}", before, after);
            IdentityLog.UserUpdated(logger, actor.UserId, user.Id, string.Join(",", changes), string.Join(",", roles));
            return null;
        });
    }

    public async Task<Result<UserDetailDto>> SetStatusAsync(string id, SetUserStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ChangeAsync(id, request.ConcurrencyStamp, "SetUserStatus", ct, async (change, actor, user, _) =>
        {
            if (user.IsActive == request.Active)
            {
                return await change.TouchAsync(user);
            }

            user.IsActive = request.Active;
            user.DeactivatedUtc = request.Active ? null : time.GetUtcNow();
            if (await change.SaveAsync(user) is { } error)
            {
                return error;
            }
            if (!request.Active)
            {
                // Ends cookies and circuits at their next validation; the resolver already denies everything.
                Ensure(await change.Users.UpdateSecurityStampAsync(user), "sign the user out");
            }
            await AuditAsync(change.Db, actor, user, request.Active ? AuditLogActions.UserReactivated : AuditLogActions.UserDeactivated,
                request.Active ? "Reactivated" : "Deactivated: signed out and blocked from signing in",
                new Dictionary<string, string?> { ["Active"] = request.Active ? "No" : "Yes" },
                new Dictionary<string, string?> { ["Active"] = request.Active ? "Yes" : "No" });
            IdentityLog.UserStatusChanged(logger, actor.UserId, user.Id, request.Active);
            return null;
        });
    }

    public async Task<Result<UserDetailDto>> SetOverridesAsync(string id, SetPermissionOverridesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Overrides);
        return await ChangeAsync(id, request.ConcurrencyStamp, "SetUserPermissions", ct, async (change, actor, user, current) =>
        {
            if (request.Overrides.Keys.Any(p => !PermissionCatalog.Contains(p)))
            {
                return Invalid(UserFieldNames.Overrides, "Choose permissions from the list.");
            }

            var changed = PermissionCatalog.All.Select(p => p.Name).Where(p =>
                current.Overrides.TryGetValue(p, out var before) != request.Overrides.TryGetValue(p, out var after) || before != after);
            if (AdministrationRules.Missing(actor, changed) is { Count: > 0 } missing)
            {
                return Invalid(UserFieldNames.Overrides,
                    $"You can only allow or deny permissions you hold yourself. You do not hold: {AdministrationRules.Labels(missing)}.");
            }

            var stored = await change.Db.UserPermissionOverrides.Where(o => o.UserId == user.Id).ToListAsync(ct);
            change.Db.UserPermissionOverrides.RemoveRange(stored);
            var now = time.GetUtcNow();
            change.Db.UserPermissionOverrides.AddRange(request.Overrides.Select(o => new AppUserPermissionOverride
            {
                UserId = user.Id, PermissionName = o.Key, Effect = o.Value, ChangedUtc = now, ChangedById = actor.UserId!,
            }));
            if (await change.SaveAsync(user) is { } error)
            {
                return error;
            }

            var changedNames = PermissionCatalog.All.Select(p => p.Name).Where(p =>
                current.Overrides.TryGetValue(p, out var b) != request.Overrides.TryGetValue(p, out var a) || b != a).ToList();
            await AuditAsync(change.Db, actor, user, AuditLogActions.UserPermissionsChanged,
                $"Overrides changed: {string.Join(", ", changedNames.Select(PermissionCatalog.Label))}",
                changedNames.ToDictionary(p => p, p => (string?)EffectText(current.Overrides, p)),
                changedNames.ToDictionary(p => p, p => (string?)EffectText(request.Overrides, p)));
            IdentityLog.UserPermissionOverridesChanged(logger, actor.UserId, user.Id,
                Names(request.Overrides, PermissionEffect.Allow), Names(request.Overrides, PermissionEffect.Deny));
            return null;
        });
    }

    public async Task<Result<UserDetailDto>> UnlockAsync(string id, string concurrencyStamp, CancellationToken ct) =>
        await ChangeAsync(id, concurrencyStamp, "UnlockUser", ct, async (change, actor, user, _) =>
        {
            var lockedUntil = user.LockoutEnd;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            if (await change.SaveAsync(user) is { } error)
            {
                return error;
            }
            await AuditAsync(change.Db, actor, user, AuditLogActions.UserUnlocked, "Sign-in lockout ended",
                new Dictionary<string, string?> { ["Locked out until"] = lockedUntil?.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
                new Dictionary<string, string?> { ["Locked out until"] = null });
            IdentityLog.UserUnlocked(logger, actor.UserId, user.Id);
            return null;
        });

    /// <summary>The scope a change runs in: the store's context and the user manager that saves through it.</summary>
    private sealed class Change(AppDbContext db, UserManager<AppUser> users)
    {
        public AppDbContext Db => db;
        public UserManager<AppUser> Users => users;

        /// <summary>Saves the user and any pending changes on the context; a stale stamp becomes <see cref="IdentityErrors.Conflict"/>.</summary>
        public async Task<Error?> SaveAsync(AppUser user)
        {
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? null
                : IdentityErrors.IsConcurrencyFailure(result) ? IdentityErrors.Conflict
                : throw new InvalidOperationException($"Saving user failed: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }

        /// <summary>For a request that changes nothing: still fails on a stale stamp.</summary>
        public Task<Error?> TouchAsync(AppUser user) => SaveAsync(user);
    }

    private delegate Task<Error?> ChangeOperation(Change change, OperationActor actor, AppUser user, StoredUserAccess current);

    /// <summary>
    /// Authorizes, loads the user with the caller's stamp as the original value (so EF detects a concurrent change),
    /// refuses users the actor may not manage, runs <paramref name="operation"/> in a transaction and returns the fresh detail.
    /// </summary>
    private async Task<Result<UserDetailDto>> ChangeAsync(string id, string? concurrencyStamp, string operationName,
        CancellationToken ct, ChangeOperation operation)
    {
        using var auditOperation = AuditOperation.BeginIfNone(logger, operationName);
        if (await AuthorizeAsync(operationName, ct) is not { } actor)
        {
            return Result<UserDetailDto>.Failure(IdentityErrors.Forbidden);
        }

        var failure = await DatabaseUnit.RunAsync(contextFactory, scopeFactory, async (services, db, token) =>
        {
            var users = services.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(id);
            var current = user is null ? null : await PermissionQueries.LoadUserAccessAsync(db, user.Id, token);
            if (user is null || current is null)
            {
                return (IdentityErrors.UserNotFound, false);
            }
            if (!string.Equals(user.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal))
            {
                return (IdentityErrors.Conflict, false);
            }

            var rolePermissions = await PermissionQueries.LoadRolePermissionsAsync(db, token);
            if (AdministrationRules.CannotManageUser(actor, user.Id, PermissionsIfActive(current, rolePermissions)) is { } reason)
            {
                IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.UsersManage, user.Id);
                return (IdentityErrors.NotAllowed(reason), false);
            }

            Error? error;
            try
            {
                error = await operation(new Change(db, users), actor, user, current);
            }
            catch (DbUpdateConcurrencyException)
            {
                error = IdentityErrors.Conflict;
            }
            return (error, error is null);
        }, ct);
        if (failure is not null)
        {
            return Result<UserDetailDto>.Failure(failure);
        }

        resolver.Invalidate();
        await using var fresh = await contextFactory.CreateDbContextAsync(ct);
        var detail = await DetailAsync(fresh, actor, id, ct);
        return detail is null ? Result<UserDetailDto>.Failure(IdentityErrors.UserNotFound) : Result<UserDetailDto>.Success(detail);
    }

    private async Task<OperationActor?> AuthorizeAsync(string operation, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (actor.HasPermission(Permissions.UsersManage))
        {
            return actor;
        }
        IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.UsersManage, operation);
        return null;
    }

    private async Task<UserDetailDto?> DetailAsync(AppDbContext db, OperationActor actor, string id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        var access = user is null ? null : await PermissionQueries.LoadUserAccessAsync(db, id, ct);
        if (user is null || access is null)
        {
            return null;
        }

        var rolePermissions = await PermissionQueries.LoadRolePermissionsAsync(db, ct);
        var effective = EffectivePermissions.Compute(user.IsActive, FromRoles(access.Roles, rolePermissions), access.Overrides);
        var permissions = PermissionCatalog.All.Select(p => new EffectivePermissionDto(
            p.Name,
            effective.Contains(p.Name),
            [.. access.Roles.Where(r => rolePermissions.TryGetValue(r, out var granted) && granted.Contains(p.Name))],
            access.Overrides.TryGetValue(p.Name, out var effect) ? effect : null)).ToList();
        var reason = AdministrationRules.CannotManageUser(actor, id, PermissionsIfActive(access, rolePermissions));

        return new UserDetailDto(user.Id, user.UserName ?? user.Id, user.IsActive, user.LockoutEnd, user.CreatedUtc,
            user.LastSignInUtc, user.DeactivatedUtc, access.Roles, access.Overrides, permissions,
            user.ConcurrencyStamp ?? string.Empty, reason is null, reason);
    }

    /// <summary>Every role, built-in ones first in matrix order, then custom roles by name.</summary>
    internal static async Task<IReadOnlyList<RoleOptionDto>> RoleOptionsAsync(AppDbContext db, OperationActor actor, CancellationToken ct)
    {
        var rolePermissions = await PermissionQueries.LoadRolePermissionsAsync(db, ct);
        var descriptions = await db.Roles.AsNoTracking().Where(r => r.Name != null)
            .ToDictionaryAsync(r => r.Name!, r => r.Description, StringComparer.Ordinal, ct);
        var builtIn = RolePermissionMap.Roles.Where(descriptions.ContainsKey);
        var custom = descriptions.Keys.Where(n => !RolePermissionMap.Roles.Contains(n)).Order(StringComparer.OrdinalIgnoreCase);
        return [.. builtIn.Concat(custom).Select(name => new RoleOptionDto(name, descriptions[name],
            AdministrationRules.Missing(actor, rolePermissions.GetValueOrDefault(name) ?? []).Count == 0))];
    }

    /// <summary>Validates the role selection: at least one existing role, and the actor may grant every role added or removed.</summary>
    private static async Task<string?> ValidateRolesAsync(AppDbContext db, OperationActor actor,
        IReadOnlyCollection<string> before, IReadOnlyCollection<string> after, CancellationToken ct)
    {
        if (after.Count == 0)
        {
            return "Choose at least one role.";
        }
        var options = (await RoleOptionsAsync(db, actor, ct)).ToDictionary(o => o.Name, StringComparer.Ordinal);
        if (after.Any(r => !options.ContainsKey(r)))
        {
            return "Choose roles from the list.";
        }
        var touched = after.Except(before, StringComparer.Ordinal).Concat(before.Except(after, StringComparer.Ordinal));
        var notAllowed = touched.Where(r => options.TryGetValue(r, out var option) && !option.Assignable).ToList();
        return notAllowed.Count == 0
            ? null
            : $"You can only add or remove roles whose permissions you hold yourself. Not allowed: {string.Join(", ", notAllowed.Select(AppRoles.DisplayName))}.";
    }

    internal static Dictionary<string, string[]> ValidateEmail(string email)
    {
        var errors = new Dictionary<string, string[]>();
        if (email.Length == 0)
        {
            errors[UserFieldNames.Email] = ["Enter an email address."];
        }
        else if (email.Length > EmailMaxLength)
        {
            errors[UserFieldNames.Email] = [$"Use at most {EmailMaxLength} characters."];
        }
        else if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
        {
            errors[UserFieldNames.Email] = ["Enter a valid email address, like name@example.test."];
        }
        return errors;
    }

    private static ImmutableHashSet<string> PermissionsIfActive(StoredUserAccess access,
        IReadOnlyDictionary<string, ImmutableHashSet<string>> rolePermissions) =>
        EffectivePermissions.Compute(true, FromRoles(access.Roles, rolePermissions), access.Overrides);

    private static IEnumerable<string> FromRoles(IEnumerable<string> roles, IReadOnlyDictionary<string, ImmutableHashSet<string>> rolePermissions) =>
        roles.SelectMany(r => rolePermissions.TryGetValue(r, out var granted) ? granted : []);

    private static List<string> Distinct(IReadOnlyList<string>? roles) =>
        [.. (roles ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Records the change in the system audit log on the transaction's context and saves it, so it commits with the
    /// change (ADR 0014). Values are emails, role and permission names and flags only.
    /// </summary>
    private async Task AuditAsync(AppDbContext db, OperationActor actor, AppUser user, string action, string summary,
        IReadOnlyDictionary<string, string?>? before, IReadOnlyDictionary<string, string?>? after)
    {
        SystemAudit.Append(db, new AuditRecord(AuditCategories.Users, action, AuditOutcome.Success, actor.UserId,
            AuditAppender.ActorName(actor), AuditEntityTypes.User, user.Id, user.UserName, summary, before, after), time.GetUtcNow());
        await db.SaveChangesAsync();
    }

    private static string RoleList(IEnumerable<string> roles) => string.Join(", ", roles.Order(StringComparer.Ordinal));

    private static string EffectText(IReadOnlyDictionary<string, PermissionEffect> overrides, string permission) =>
        overrides.TryGetValue(permission, out var effect) ? effect.ToString() : "Inherit";

    private static Error Invalid(string field, string message) =>
        IdentityErrors.Validation(new Dictionary<string, string[]> { [field] = [message] });

    private static string Names(IReadOnlyDictionary<string, PermissionEffect> overrides, PermissionEffect effect) =>
        string.Join(",", overrides.Where(o => o.Value == effect).Select(o => o.Key).Order(StringComparer.Ordinal));

    private static void Ensure(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            // Error codes only: descriptions can echo submitted values. The transaction rolls back.
            throw new InvalidOperationException($"Failed to {operation}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }
}
