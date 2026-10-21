using System.Security.Claims;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Administration;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Account;

/// <summary>
/// Changes the signed-in user's own password for the static SSR change-password page. The current password is checked
/// with lockout, like sign-in, so the page cannot be used to guess it. Success changes the security stamp, which ends the
/// user's other sessions within the revalidation interval, and refreshes this browser's cookie so it stays signed in.
/// Request-scoped: it uses the request's <see cref="SignInManager{TUser}"/>.
/// </summary>
public sealed class AccountPasswordChange(
    SignInManager<AppUser> signInManager,
    AuditLogWriter audit,
    ILogger<AccountPasswordChange> logger)
{
    public const string LockedOutMessage =
        "Too many incorrect passwords. Your account is locked for five minutes; then try again.";

    public async Task<Result<bool>> ChangeAsync(ClaimsPrincipal principal, string? currentPassword, string? newPassword)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(currentPassword))
        {
            errors[UserFieldNames.CurrentPassword] = ["Enter your current password."];
        }
        if (string.IsNullOrEmpty(newPassword))
        {
            errors[UserFieldNames.NewPassword] = ["Enter a new password."];
        }
        if (errors.Count > 0)
        {
            return Result<bool>.Failure(IdentityErrors.Validation(errors));
        }

        var users = signInManager.UserManager;
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Result<bool>.Failure(IdentityErrors.Forbidden);
        }

        var check = await signInManager.CheckPasswordSignInAsync(user, currentPassword!, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            IdentityLog.PasswordChangeFailed(logger, user.Id, check.IsLockedOut ? "LockedOut" : "InvalidCurrentPassword");
            await AuthenticationAudit.WriteAsync(audit, AuditLogActions.PasswordChangeFailed, AuditOutcome.Failure, user.Id, user.UserName,
                check.IsLockedOut ? "Password change refused: locked out after failed attempts" : "Password change failed: wrong current password");
            errors[UserFieldNames.CurrentPassword] = [check.IsLockedOut ? LockedOutMessage : "Your current password is not correct."];
            return Result<bool>.Failure(IdentityErrors.Validation(errors));
        }

        var changed = await users.ChangePasswordAsync(user, currentPassword!, newPassword!);
        if (!changed.Succeeded)
        {
            IdentityLog.PasswordChangeFailed(logger, user.Id, "PasswordRules");
            await AuthenticationAudit.WriteAsync(audit, AuditLogActions.PasswordChangeFailed, AuditOutcome.Failure, user.Id, user.UserName,
                "Password change failed: the new password does not meet the rules");
            var messages = IdentityErrors.PasswordMessages(changed);
            errors[UserFieldNames.NewPassword] = messages.Length > 0 ? messages : ["The password could not be changed."];
            return Result<bool>.Failure(IdentityErrors.Validation(errors));
        }

        await signInManager.RefreshSignInAsync(user);
        IdentityLog.PasswordChanged(logger, user.Id);
        await AuthenticationAudit.WriteAsync(audit, AuditLogActions.PasswordChanged, AuditOutcome.Success, user.Id, user.UserName, "Password changed; other sessions end within a minute");
        return Result<bool>.Success(true);
    }
}
