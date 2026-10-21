using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using Microsoft.AspNetCore.Identity;

namespace CaseIntake.Identity.Administration;

/// <summary>Errors of user and role administration and password changes. Messages are safe to show and never echo submitted values.</summary>
internal static class IdentityErrors
{
    public static Error Forbidden { get; } =
        new(ErrorKind.Forbidden, "Forbidden", "You do not have permission to perform this action.");

    public static Error UserNotFound { get; } =
        new(ErrorKind.NotFound, "UserNotFound", "This user no longer exists.");

    public static Error RoleNotFound { get; } =
        new(ErrorKind.NotFound, "RoleNotFound", "This role no longer exists.");

    public static Error Conflict { get; } =
        new(ErrorKind.Conflict, "Conflict", "Someone else changed this while you were editing. Your changes were not saved; the latest version is shown.");

    /// <summary>The action is understood but not allowed for this user or role (own account, more access than the actor, built-in role...).</summary>
    public static Error NotAllowed(string message) => new(ErrorKind.Forbidden, "NotAllowed", message);

    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new(ErrorKind.Validation, "Validation", "Some fields need attention.", fieldErrors);

    /// <summary>Maps a failed <see cref="UserManager{TUser}.CreateAsync(TUser, string)"/> or user name change to field errors.</summary>
    public static Error FromCreate(IdentityResult result)
    {
        var errors = new Dictionary<string, string[]>();
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail)))
        {
            errors[UserFieldNames.Email] = ["A user with this email address already exists."];
        }
        else if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.InvalidUserName) or nameof(IdentityErrorDescriber.InvalidEmail)))
        {
            errors[UserFieldNames.Email] = ["Enter a valid email address, like name@example.test."];
        }
        if (PasswordMessages(result) is { Length: > 0 } password)
        {
            errors[UserFieldNames.Password] = password;
        }
        if (errors.Count == 0)
        {
            errors[UserFieldNames.Email] = ["The user could not be saved."];
        }
        return Validation(errors);
    }

    public static bool IsConcurrencyFailure(IdentityResult result) =>
        result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));

    /// <summary>Identity's password rule messages (they describe the rule, never the password).</summary>
    public static string[] PasswordMessages(IdentityResult result) =>
        [.. result.Errors.Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal)).Select(e => e.Description)];
}
