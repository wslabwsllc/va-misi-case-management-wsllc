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
        Message = "User {ActorId} created user {UserId} with roles {Roles}")]
    public static partial void UserCreated(ILogger logger, string? actorId, string userId, string roles);

    /// <param name="changes">Which parts changed: Email, Roles or both (never the values themselves).</param>
    [LoggerMessage(EventId = 3014, EventName = "UserUpdated", Level = LogLevel.Information,
        Message = "User {ActorId} updated user {UserId}: {Changes}; roles now {Roles}")]
    public static partial void UserUpdated(ILogger logger, string? actorId, string userId, string changes, string roles);

    [LoggerMessage(EventId = 3015, EventName = "UserStatusChanged", Level = LogLevel.Information,
        Message = "User {ActorId} set user {UserId} active = {IsActive}")]
    public static partial void UserStatusChanged(ILogger logger, string? actorId, string userId, bool isActive);

    [LoggerMessage(EventId = 3016, EventName = "UserPermissionOverridesChanged", Level = LogLevel.Information,
        Message = "User {ActorId} set the permission overrides of user {UserId}: allow {Allowed}; deny {Denied}")]
    public static partial void UserPermissionOverridesChanged(ILogger logger, string? actorId, string userId, string allowed, string denied);

    [LoggerMessage(EventId = 3017, EventName = "UserUnlocked", Level = LogLevel.Information,
        Message = "User {ActorId} unlocked user {UserId}")]
    public static partial void UserUnlocked(ILogger logger, string? actorId, string userId);

    [LoggerMessage(EventId = 3020, EventName = "RoleCreated", Level = LogLevel.Information,
        Message = "User {ActorId} created role {RoleId} ({RoleName}) with permissions {Permissions}")]
    public static partial void RoleCreated(ILogger logger, string? actorId, string roleId, string roleName, string permissions);

    [LoggerMessage(EventId = 3021, EventName = "RoleUpdated", Level = LogLevel.Information,
        Message = "User {ActorId} updated role {RoleId} ({RoleName}): added {Added}; removed {Removed}")]
    public static partial void RoleUpdated(ILogger logger, string? actorId, string roleId, string roleName, string added, string removed);

    [LoggerMessage(EventId = 3022, EventName = "RoleDeleted", Level = LogLevel.Information,
        Message = "User {ActorId} deleted role {RoleId} ({RoleName})")]
    public static partial void RoleDeleted(ILogger logger, string? actorId, string roleId, string roleName);

    [LoggerMessage(EventId = 3012, EventName = "PasswordChanged", Level = LogLevel.Information,
        Message = "User {UserId} changed their password")]
    public static partial void PasswordChanged(ILogger logger, string userId);

    /// <param name="reason">InvalidCurrentPassword, LockedOut or PasswordRules.</param>
    [LoggerMessage(EventId = 3013, EventName = "PasswordChangeFailed", Level = LogLevel.Warning,
        Message = "Password change failed for {UserId}: {Reason}")]
    public static partial void PasswordChangeFailed(ILogger logger, string userId, string reason);

    [LoggerMessage(EventId = 3010, EventName = "IdentitySeeded", Level = LogLevel.Information,
        Message = "Identity seed created {RolesCreated} roles and {UsersCreated} users; {MembershipsAdded} role memberships and {GrantsAdded} role permissions added")]
    public static partial void IdentitySeeded(ILogger logger, int rolesCreated, int usersCreated, int membershipsAdded, int grantsAdded);
}
