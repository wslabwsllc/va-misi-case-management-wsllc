using CaseIntake.Contracts.Security;
using Microsoft.AspNetCore.Http;

namespace CaseIntake.Identity.CurrentUser;

/// <summary>
/// <see cref="ICurrentUser"/> for minimal API endpoints: reflects the current request's <c>HttpContext.User</c>.
/// Registered only under the <see cref="CurrentUserSources.Http"/> key; endpoints resolve it explicitly and must not
/// hand it to anything that outlives the request.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Task<OperationActor> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("The HTTP current-user adapter was used outside an HTTP request.");
        return Task.FromResult(OperationActorFactory.FromPrincipal(httpContext.User));
    }
}
