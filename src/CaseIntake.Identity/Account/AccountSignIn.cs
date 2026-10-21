using CaseIntake.Contracts.Auditing;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Auditing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Account;

public enum SignInOutcome { Succeeded, Failed, LockedOut }

/// <summary>
/// Password sign-in for the static SSR login page. Every failure maps to the same generic message in the UI (no account
/// enumeration); the log records the reason and, when the account exists, its user ID. Failed attempts count toward
/// lockout (five attempts, five minutes). A deactivated account is refused before its password is checked, so it never
/// signs in and never accumulates lockout attempts. Request-scoped: it uses the request's <see cref="SignInManager{TUser}"/>.
/// </summary>
public sealed class AccountSignIn(
    SignInManager<AppUser> signInManager,
    IDbContextFactory<AppDbContext> contextFactory,
    AuditLogWriter audit,
    TimeProvider time,
    ILogger<AccountSignIn> logger)
{
    public async Task<SignInOutcome> PasswordSignInAsync(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            IdentityLog.LoginFailed(logger, null, "InvalidInput");
            await AuthenticationAudit.WriteAsync(audit, AuditLogActions.SignInFailed, AuditOutcome.Failure, null, null, "Sign-in failed: email or password missing");
            return SignInOutcome.Failed;
        }

        var user = await signInManager.UserManager.FindByNameAsync(userName.Trim());
        if (user is null)
        {
            IdentityLog.LoginFailed(logger, null, "InvalidCredentials");
            await AuthenticationAudit.WriteAsync(audit, AuditLogActions.SignInFailed, AuditOutcome.Failure, null, null, "Sign-in failed for an unknown account");
            return SignInOutcome.Failed;
        }
        if (!user.IsActive)
        {
            IdentityLog.LoginFailed(logger, user.Id, "Inactive");
            await AuthenticationAudit.WriteAsync(audit, AuditLogActions.SignInFailed, AuditOutcome.Denied, user.Id, user.UserName, "Sign-in refused: the account is deactivated");
            return SignInOutcome.Failed;
        }

        var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // A direct column update: going through UserManager would change the concurrency stamp and make an
            // administrator's open edit of this user fail as a conflict just because the user signed in.
            await using var db = await contextFactory.CreateDbContextAsync();
            await db.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSignInUtc, time.GetUtcNow()));
            IdentityLog.LoginSucceeded(logger, user.Id);
            await AuthenticationAudit.WriteAsync(audit, AuditLogActions.SignedIn, AuditOutcome.Success, user.Id, user.UserName, "Signed in");
            return SignInOutcome.Succeeded;
        }
        if (result.IsLockedOut)
        {
            IdentityLog.LoginLockedOut(logger, user.Id, await signInManager.UserManager.GetLockoutEndDateAsync(user));
            await AuthenticationAudit.WriteAsync(audit, AuditLogActions.SignInLockedOut, AuditOutcome.Denied, user.Id, user.UserName, "Sign-in refused: the account is locked out after failed attempts");
            return SignInOutcome.LockedOut;
        }

        IdentityLog.LoginFailed(logger, user.Id, result.IsNotAllowed ? "NotAllowed" : "InvalidCredentials");
        await AuthenticationAudit.WriteAsync(audit, AuditLogActions.SignInFailed, AuditOutcome.Failure, user.Id, user.UserName,
            result.IsNotAllowed ? "Sign-in not allowed" : "Sign-in failed: wrong password");
        return SignInOutcome.Failed;
    }
}
