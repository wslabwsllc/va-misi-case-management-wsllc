using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;

namespace CaseIntake.Contracts.Security;

/// <summary>A new local account: the email is also the user name; <paramref name="Roles"/> holds at least one role name.</summary>
public sealed record CreateUserRequest(string Email, IReadOnlyList<string> Roles, string Password);

public enum UserStatusFilter { All, Active, Inactive }

/// <summary>
/// User list query. <paramref name="Search"/> matches part of the email (case-insensitive); <paramref name="Role"/> keeps
/// members of that role. Ordered by email.
/// </summary>
public sealed record UserListQuery(string? Search = null, string? Role = null, UserStatusFilter Status = UserStatusFilter.All,
    int Page = 1, int PageSize = 25)
{
    public const int MaxPageSize = 100;
}

/// <summary>A user as the administration list shows them. <see cref="Roles"/> is empty for a user with no role.</summary>
public sealed record UserSummaryDto(string Id, string UserName, IReadOnlyList<string> Roles,
    bool IsActive = true, bool IsLockedOut = false, DateTimeOffset? LastSignInUtc = null);

/// <summary>
/// One permission on the user detail page: whether the user holds it, which of their roles grant it and the user's own
/// override, if any.
/// </summary>
public sealed record EffectivePermissionDto(string Permission, bool Granted, IReadOnlyList<string> FromRoles, PermissionEffect? Override);

/// <summary>
/// A user with everything the detail page shows. <see cref="CanManage"/> is false when the actor may only look (the
/// user is the actor, or holds a permission the actor does not); <see cref="CannotManageReason"/> then says why.
/// <see cref="ConcurrencyStamp"/> must accompany every change.
/// </summary>
public sealed record UserDetailDto(
    string Id,
    string UserName,
    bool IsActive,
    DateTimeOffset? LockoutEndUtc,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? LastSignInUtc,
    DateTimeOffset? DeactivatedUtc,
    IReadOnlyList<string> Roles,
    IReadOnlyDictionary<string, PermissionEffect> Overrides,
    IReadOnlyList<EffectivePermissionDto> Permissions,
    string ConcurrencyStamp,
    bool CanManage,
    string? CannotManageReason)
{
    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } end && end > now;
}

/// <summary>A role the add and edit forms offer. <see cref="Assignable"/> is false when it grants more than the actor holds.</summary>
public sealed record RoleOptionDto(string Name, string? Description, bool Assignable);

public sealed record UpdateUserRequest(string Email, IReadOnlyList<string> Roles, string ConcurrencyStamp);

public sealed record SetUserStatusRequest(bool Active, string ConcurrencyStamp);

/// <summary>The complete set of the user's overrides; permissions left out inherit from the user's roles.</summary>
public sealed record SetPermissionOverridesRequest(IReadOnlyDictionary<string, PermissionEffect> Overrides, string ConcurrencyStamp);

/// <summary>The field names <see cref="Error.FieldErrors"/> uses for user administration and password changes.</summary>
public static class UserFieldNames
{
    public const string Email = nameof(CreateUserRequest.Email);
    public const string Roles = nameof(CreateUserRequest.Roles);
    public const string Password = nameof(CreateUserRequest.Password);
    public const string Overrides = nameof(SetPermissionOverridesRequest.Overrides);
    public const string CurrentPassword = "CurrentPassword";
    public const string NewPassword = "NewPassword";
}

/// <summary>
/// User administration (<see cref="Permissions.UsersManage"/>), ADR 0013. Every change is limited to the actor's own
/// access, is refused for the actor's own account and checks the user's concurrency stamp (a stale stamp is a
/// <see cref="ErrorKind.Conflict"/>).
/// </summary>
public interface IUserAdministrationService
{
    Task<Result<PagedResult<UserSummaryDto>>> ListAsync(UserListQuery query, CancellationToken ct);

    /// <summary>Every role, marking the ones the actor may grant.</summary>
    Task<Result<IReadOnlyList<RoleOptionDto>>> ListRoleOptionsAsync(CancellationToken ct);

    Task<Result<UserSummaryDto>> CreateAsync(CreateUserRequest request, CancellationToken ct);

    Task<Result<UserDetailDto>> GetAsync(string id, CancellationToken ct);

    /// <summary>Changes the email (and user name) and the roles; signs the user out of their other sessions.</summary>
    Task<Result<UserDetailDto>> UpdateAsync(string id, UpdateUserRequest request, CancellationToken ct);

    /// <summary>Deactivates (signs the user out within the revalidation interval and blocks sign-in) or reactivates.</summary>
    Task<Result<UserDetailDto>> SetStatusAsync(string id, SetUserStatusRequest request, CancellationToken ct);

    Task<Result<UserDetailDto>> SetOverridesAsync(string id, SetPermissionOverridesRequest request, CancellationToken ct);

    /// <summary>Ends a lockout after too many failed sign-ins and resets the failed-attempt count.</summary>
    Task<Result<UserDetailDto>> UnlockAsync(string id, string concurrencyStamp, CancellationToken ct);
}
