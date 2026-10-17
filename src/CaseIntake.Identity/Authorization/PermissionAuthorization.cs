using CaseIntake.Contracts.Security;
using CaseIntake.Identity.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Authorization;

/// <summary>Requires one permission from <see cref="RolePermissionMap"/>. The policy name equals the permission name.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Succeeds when the authenticated principal's trusted role claims (issued by Identity at sign-in) grant the permission.
/// Uses the same <see cref="OperationActorFactory"/> snapshot as the services, so pages, endpoints and services agree.
/// Does not log: <c>AuthorizeView</c> evaluates policies just to decide what to show, which is not a denial.
/// </summary>
internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (OperationActorFactory.FromPrincipal(context.User).HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Logs <c>AuthorizationDenied</c> for every HTTP request the authorization middleware forbids (an authenticated user
/// lacking a permission), then defers to the default behavior (403 for endpoints, redirect to the access-denied page
/// for pages).
/// </summary>
internal sealed class DenialLoggingAuthorizationResultHandler(ILogger<DenialLoggingAuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var userId = OperationActorFactory.FromPrincipal(context.User).UserId;
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
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
