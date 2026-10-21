using CaseIntake.Contracts.Common;

namespace CaseIntake.Contracts.Security;

/// <summary>
/// A role as the Roles page lists it. Built-in roles (<see cref="RolePermissionMap.Roles"/>) cannot be renamed or
/// deleted; a locked role (Administrator) also keeps every permission.
/// </summary>
public sealed record RoleSummaryDto(string Id, string Name, string? Description, bool IsBuiltIn, bool IsLocked,
    int MemberCount, IReadOnlyList<string> Permissions);

/// <summary>
/// A role with its members. <see cref="CanManage"/> is false when the role is locked or grants a permission the actor does
/// not hold; <see cref="CannotManageReason"/> says why.
/// </summary>
public sealed record RoleDetailDto(string Id, string Name, string? Description, bool IsBuiltIn, bool IsLocked,
    IReadOnlyList<string> Permissions, IReadOnlyList<string> Members, string ConcurrencyStamp,
    bool CanManage, string? CannotManageReason);

/// <summary>
/// Creates a role or replaces one's name, description and permissions. <paramref name="ConcurrencyStamp"/> is required for
/// an update; a built-in role keeps its name whatever is sent.
/// </summary>
public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions, string? ConcurrencyStamp = null);

public static class RoleFieldNames
{
    public const string Name = nameof(SaveRoleRequest.Name);
    public const string Description = nameof(SaveRoleRequest.Description);
    public const string Permissions = nameof(SaveRoleRequest.Permissions);

    public const int NameMaxLength = 64;
    public const int DescriptionMaxLength = 500;
}

/// <summary>
/// Role administration (<see cref="Permissions.RolesManage"/>), ADR 0013. Changes take effect at the members' next
/// permission check; nobody adds or removes a permission they do not hold, or removes their own ability to manage roles.
/// </summary>
public interface IRoleAdministrationService
{
    Task<Result<IReadOnlyList<RoleSummaryDto>>> ListAsync(CancellationToken ct);

    Task<Result<RoleDetailDto>> GetAsync(string id, CancellationToken ct);

    Task<Result<RoleDetailDto>> CreateAsync(SaveRoleRequest request, CancellationToken ct);

    Task<Result<RoleDetailDto>> UpdateAsync(string id, SaveRoleRequest request, CancellationToken ct);

    /// <summary>Deletes a custom role that has no members; returns the deleted role's name.</summary>
    Task<Result<string>> DeleteAsync(string id, string concurrencyStamp, CancellationToken ct);
}
