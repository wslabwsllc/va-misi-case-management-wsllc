using CaseIntake.Contracts.Common;

namespace CaseIntake.Contracts.Security;

/// <summary>A new local account: the email is also the user name; <paramref name="Role"/> is one of <see cref="RolePermissionMap.Roles"/>.</summary>
public sealed record CreateUserRequest(string Email, string Role, string Password);

/// <summary>A user as the administration page lists them. <see cref="Roles"/> is empty for a user with no role.</summary>
public sealed record UserSummaryDto(string Id, string UserName, IReadOnlyList<string> Roles);

/// <summary>The field names <see cref="Error.FieldErrors"/> uses for user administration and password changes.</summary>
public static class UserFieldNames
{
    public const string Email = nameof(CreateUserRequest.Email);
    public const string Role = nameof(CreateUserRequest.Role);
    public const string Password = nameof(CreateUserRequest.Password);
    public const string CurrentPassword = "CurrentPassword";
    public const string NewPassword = "NewPassword";
}

/// <summary>User administration (<see cref="Permissions.UsersManage"/>): list users and add a user with one role.</summary>
public interface IUserAdministrationService
{
    Task<Result<IReadOnlyList<UserSummaryDto>>> ListAsync(CancellationToken ct);

    Task<Result<UserSummaryDto>> CreateAsync(CreateUserRequest request, CancellationToken ct);
}
