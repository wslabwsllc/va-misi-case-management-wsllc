using CaseIntake.Contracts.Security;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity;

/// <summary>
/// E3 log events (EventId range 3000-3999). Only safe fields are logged: Identity user IDs, role and permission names,
/// request paths and failure reasons. Passwords, password hashes, cookies and attempted user names are never logged.
/// </summary>
public static partial class IdentityLog
{
    [LoggerMessage(EventId = 3001, EventName = "LoginSucceeded", Level = LogLevel.Information,
        Message = "User {UserId} signed in")]
    public static partial void LoginSucceeded(ILogger logger, string userId);

    /// <param name="userId">The Identity user ID when the account exists, otherwise <c>null</c>.</param>
    /// <param name="reason">InvalidCredentials, NotAllowed or InvalidInput.</param>
    [LoggerMessage(EventId = 3002, EventName = "LoginFailed", Level = LogLevel.Warning,
        Message = "Sign-in failed for {UserId}: {Reason}")]
    public static partial void LoginFailed(ILogger logger, string? userId, string reason);

    [LoggerMessage(EventId = 3003, EventName = "LoginLockedOut", Level = LogLevel.Warning,
        Message = "Sign-in rejected for {UserId}: account is locked out until {LockoutEndUtc}")]
    public static partial void LoginLockedOut(ILogger logger, string userId, DateTimeOffset? lockoutEndUtc);

    [LoggerMessage(EventId = 3004, EventName = "LoggedOut", Level = LogLevel.Information,
        Message = "User {UserId} signed out")]
    public static partial void LoggedOut(ILogger logger, string? userId);

    [LoggerMessage(EventId = SecurityEvents.AuthorizationDeniedId, EventName = SecurityEvents.AuthorizationDeniedName,
        Level = LogLevel.Warning, Message = "Authorization denied for {UserId}: {Permission} on {ResourceId}")]
    public static partial void AuthorizationDenied(ILogger logger, string? userId, string permission, string? resourceId);

    [LoggerMessage(EventId = 3006, EventName = "CircuitAuthenticationRevoked", Level = LogLevel.Information,
        Message = "Interactive session for {UserId} is no longer valid and was signed out")]
    public static partial void CircuitAuthenticationRevoked(ILogger logger, string? userId);

    [LoggerMessage(EventId = 3011, EventName = "UserCreated", Level = LogLevel.Information,
        Message = "User {ActorId} created user {UserId} with role {Role}")]
    public static partial void UserCreated(ILogger logger, string? actorId, string userId, string role);

    [LoggerMessage(EventId = 3012, EventName = "PasswordChanged", Level = LogLevel.Information,
        Message = "User {UserId} changed their password")]
    public static partial void PasswordChanged(ILogger logger, string userId);

    /// <param name="reason">InvalidCurrentPassword, LockedOut or PasswordRules.</param>
    [LoggerMessage(EventId = 3013, EventName = "PasswordChangeFailed", Level = LogLevel.Warning,
        Message = "Password change failed for {UserId}: {Reason}")]
    public static partial void PasswordChangeFailed(ILogger logger, string userId, string reason);

    [LoggerMessage(EventId = 3010, EventName = "IdentitySeeded", Level = LogLevel.Information,
        Message = "Identity seed created {RolesCreated} roles and {UsersCreated} users; {MembershipsAdded} role memberships added")]
    public static partial void IdentitySeeded(ILogger logger, int rolesCreated, int usersCreated, int membershipsAdded);
}
