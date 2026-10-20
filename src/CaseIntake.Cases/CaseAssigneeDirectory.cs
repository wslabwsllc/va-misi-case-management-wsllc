using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Cases;

/// <summary>
/// Users who can own a case (issue #22): members of a role whose permissions include <c>cases.edit</c> (from the E3
/// matrix, not a hard-coded list), read from the Identity tables. Ordered by user name.
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

    /// <summary>The roles whose members can own cases.</summary>
    internal static string[] AssignableRoles { get; } =
        [.. RolePermissionMap.Roles.Where(r => RolePermissionMap.Grants([r], Permissions.CasesEdit))];

    /// <summary>One entry per user (the first assignable role by name when a user has several).</summary>
    internal static async Task<IReadOnlyList<AssigneeDto>> QueryAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            join user in db.Users on userRole.UserId equals user.Id
            where role.Name != null && AssignableRoles.Contains(role.Name) && user.UserName != null
            select new { UserName = user.UserName!, Role = role.Name! })
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(r => r.UserName, StringComparer.Ordinal)
            .Select(g => new AssigneeDto(g.Key, g.Select(r => r.Role).Order(StringComparer.Ordinal).First()))
            .OrderBy(a => a.UserName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>True when <paramref name="userName"/> may own a case.</summary>
    internal static async Task<bool> IsAssignableAsync(AppDbContext db, string userName, CancellationToken ct)
    {
        var normalized = userName.ToUpperInvariant();
        return await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            join user in db.Users on userRole.UserId equals user.Id
            where role.Name != null && AssignableRoles.Contains(role.Name) && user.NormalizedUserName == normalized
            select user.Id).AnyAsync(ct);
    }
}
