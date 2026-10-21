using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.CurrentUser;
using CaseIntake.Persistence.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Authorization;

/// <summary>Requires one permission (<see cref="RolePermissionMap.AllPermissions"/>). The policy name equals the permission name.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Succeeds when the principal holds the permission according to <see cref="PermissionResolver"/> (stored role
/// permissions and the user's stored active flag and overrides). Uses the same snapshot as the services, so pages,
/// endpoints and services agree. Does not log: <c>AuthorizeView</c> evaluates policies just to decide what to show,
/// which is not a denial.
/// </summary>
internal sealed class PermissionAuthorizationHandler(PermissionResolver resolver) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var actor = await resolver.ResolveAsync(context.User, CancellationToken.None);
        if (actor.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>
/// Logs <c>AuthorizationDenied</c> and records an <c>AccessDenied</c> audit entry (ADR 0014) for every HTTP request the
/// authorization middleware forbids (an authenticated user lacking a permission), then defers to the default behavior
/// (403 for endpoints, redirect to the access-denied page for pages).
/// </summary>
internal sealed class DenialLoggingAuthorizationResultHandler(ILogger<DenialLoggingAuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var userId = OperationActorFactory.UserId(context.User);
            var permissions = authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<PermissionRequirement>()
                .Select(r => r.Permission).ToList() ?? [];
            if (permissions.Count == 0)
            {
                permissions.Add("(policy)");
            }
            foreach (var permission in permissions)
            {
                IdentityLog.AuthorizationDenied(logger, userId, permission, context.Request.Path.Value);
            }
            // Optional: hosts without persistence (unit tests) have no writer.
            if (context.RequestServices.GetService<AuditLogWriter>() is { } audit)
            {
                await AccessDeniedAudit.WriteAsync(audit, userId, context.User.Identity?.Name, context.Request.Path.Value, permissions);
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

/// <summary>An access denial in the system audit log (ADR 0014): who, which page or endpoint, which permissions were missing.</summary>
public static class AccessDeniedAudit
{
    public static Task WriteAsync(AuditLogWriter writer, string? userId, string? userName, string? resource, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(permissions);
        return writer.WriteAsync(new AuditRecord(AuditCategories.Authorization, AuditLogActions.AccessDenied, AuditOutcome.Denied,
            userId, userName, AuditEntityTypes.Page, resource, resource,
            $"Access denied; missing: {string.Join(", ", permissions.Select(PermissionCatalog.Label))}"), CancellationToken.None);
    }
}
