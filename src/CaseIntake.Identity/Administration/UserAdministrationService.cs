using System.Net.Mail;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Administration;

/// <summary>
/// Supervisors list users and add a user with one role (any of <see cref="RolePermissionMap.Roles"/>). Runs in the
/// circuit, so <see cref="UserManager{TUser}"/> is resolved in a fresh scope per call and never held by the circuit
/// (ADR 0007). New accounts are confirmed and lockout-enabled like the demo users; the user changes the initial
/// password on the change-password page.
/// </summary>
internal sealed class UserAdministrationService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    ILogger<UserAdministrationService> logger) : IUserAdministrationService
{
    /// <summary>Identity's default user name and email column length.</summary>
    internal const int EmailMaxLength = 256;

    public async Task<Result<IReadOnlyList<UserSummaryDto>>> ListAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.UsersManage))
        {
            IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.UsersManage, "ListUsers");
            return Result<IReadOnlyList<UserSummaryDto>>.Failure(IdentityErrors.Forbidden);
        }

        // shortcut: loads every user in one list; add paging if the user count grows into the thousands.
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var users = await db.Users.Where(u => u.UserName != null).Select(u => new { u.Id, UserName = u.UserName! }).ToListAsync(ct);
        var roles = (await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where role.Name != null
            select new { userRole.UserId, Name = role.Name! }).ToListAsync(ct))
            .ToLookup(r => r.UserId, r => r.Name);

        return Result<IReadOnlyList<UserSummaryDto>>.Success([.. users
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new UserSummaryDto(u.Id, u.UserName, [.. roles[u.Id].Order(StringComparer.Ordinal)]))]);
    }

    public async Task<Result<UserSummaryDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.UsersManage))
        {
            IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.UsersManage, "CreateUser");
            return Result<UserSummaryDto>.Failure(IdentityErrors.Forbidden);
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (Validate(email, request.Role, request.Password) is { Count: > 0 } invalid)
        {
            return Result<UserSummaryDto>.Failure(IdentityErrors.Validation(invalid));
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, LockoutEnabled = true };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return Result<UserSummaryDto>.Failure(IdentityErrors.FromCreate(created));
        }

        var added = await users.AddToRoleAsync(user, request.Role);
        if (!added.Succeeded)
        {
            // Never leave an account without its role behind.
            await users.DeleteAsync(user);
            throw new InvalidOperationException(
                $"Adding the new user to role {request.Role} failed: {string.Join(", ", added.Errors.Select(e => e.Code))}");
        }

        IdentityLog.UserCreated(logger, actor.UserId, user.Id, request.Role);
        return Result<UserSummaryDto>.Success(new UserSummaryDto(user.Id, email, [request.Role]));
    }

    internal static Dictionary<string, string[]> Validate(string email, string? role, string? password)
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
        if (role is null || !RolePermissionMap.Roles.Contains(role))
        {
            errors[UserFieldNames.Role] = ["Choose a role."];
        }
        if (string.IsNullOrEmpty(password))
        {
            errors[UserFieldNames.Password] = ["Enter an initial password."];
        }
        return errors;
    }
}
