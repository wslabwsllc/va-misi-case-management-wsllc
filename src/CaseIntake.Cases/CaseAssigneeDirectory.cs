using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Cases;

/// <summary>
/// Users who can own a case (issue #22): active users who hold <c>cases.edit</c> through their stored roles and
/// permission overrides (ADR 0013, <see cref="PermissionQueries"/>), not a hard-coded list. Ordered by user name.
/// </summary>
public sealed class CaseAssigneeDirectory(
    IDbContextFactory<AppDbContext> contextFactory,
    ICurrentUser currentUser,
    ILogger<CaseAssigneeDirectory> logger) : ICaseAssigneeDirectory
{
    public async Task<Result<IReadOnlyList<AssigneeDto>>> ListAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.CasesView))
        {
            CaseLog.AuthorizationDenied(logger, actor.UserId, Permissions.CasesView, null, "ListAssignees");
            return Result<IReadOnlyList<AssigneeDto>>.Failure(CaseErrors.Forbidden);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return Result<IReadOnlyList<AssigneeDto>>.Success(await QueryAsync(db, ct));
    }

    /// <summary>
    /// One entry per user, labelled with their first role (by name) that grants <c>cases.edit</c>, or their first role
    /// when only an override grants it.
    /// </summary>
    internal static async Task<IReadOnlyList<AssigneeDto>> QueryAsync(AppDbContext db, CancellationToken ct)
    {
        var ids = await PermissionQueries.ActiveUserIdsHoldingAsync(db, Permissions.CasesEdit, ct);
        var rolePermissions = await PermissionQueries.LoadRolePermissionsAsync(db, ct);
        var rows = await (
            from user in db.Users
            where ids.Contains(user.Id) && user.UserName != null
            select new
            {
                UserName = user.UserName!,
                Roles = (from userRole in db.UserRoles
                         join role in db.Roles on userRole.RoleId equals role.Id
                         where userRole.UserId == user.Id && role.Name != null
                         select role.Name!).ToList(),
            }).ToListAsync(ct);

        return [.. rows
            .Select(r =>
            {
                var roles = r.Roles.Order(StringComparer.Ordinal).ToList();
                var label = roles.FirstOrDefault(role => rolePermissions.TryGetValue(role, out var granted) && granted.Contains(Permissions.CasesEdit))
                    ?? roles.FirstOrDefault() ?? string.Empty;
                return new AssigneeDto(r.UserName, label);
            })
            .OrderBy(a => a.UserName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>True when <paramref name="userName"/> may own a case.</summary>
    internal static async Task<bool> IsAssignableAsync(AppDbContext db, string userName, CancellationToken ct)
    {
        var normalized = userName.ToUpperInvariant();
        var userId = await db.Users.Where(u => u.NormalizedUserName == normalized).Select(u => u.Id).SingleOrDefaultAsync(ct);
        return userId is not null && (await PermissionQueries.ActiveUserIdsHoldingAsync(db, Permissions.CasesEdit, ct, userId)).Count > 0;
    }
}
