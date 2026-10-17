using CaseIntake.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Account;

public enum SignInOutcome { Succeeded, Failed, LockedOut }

/// <summary>
/// Password sign-in for the static SSR login page. Every failure maps to the same generic message in the UI (no account
/// enumeration); the log records the reason and, when the account exists, its user ID. Failed attempts count toward
/// lockout (five attempts, five minutes). Request-scoped: it uses the request's <see cref="SignInManager{TUser}"/>.
/// </summary>
public sealed class AccountSignIn(
    SignInManager<AppUser> signInManager,
    ILogger<AccountSignIn> logger)
{
    public async Task<SignInOutcome> PasswordSignInAsync(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            IdentityLog.LoginFailed(logger, null, "InvalidInput");
            return SignInOutcome.Failed;
        }

        var user = await signInManager.UserManager.FindByNameAsync(userName.Trim());
        if (user is null)
        {
            IdentityLog.LoginFailed(logger, null, "InvalidCredentials");
            return SignInOutcome.Failed;
        }

        var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            IdentityLog.LoginSucceeded(logger, user.Id);
            return SignInOutcome.Succeeded;
        }
        if (result.IsLockedOut)
        {
            IdentityLog.LoginLockedOut(logger, user.Id, await signInManager.UserManager.GetLockoutEndDateAsync(user));
            return SignInOutcome.LockedOut;
        }

        IdentityLog.LoginFailed(logger, user.Id, result.IsNotAllowed ? "NotAllowed" : "InvalidCredentials");
        return SignInOutcome.Failed;
    }
}
