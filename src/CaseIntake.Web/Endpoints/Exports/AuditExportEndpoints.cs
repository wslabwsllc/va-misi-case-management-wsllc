using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Auditing;
using CaseIntake.Web.Components.Pages.Admin;

namespace CaseIntake.Web.Endpoints.Exports;

/// <summary>
/// CSV export of the audit log (ADR 0014) with the audit log page's query-string filters. The service is bound to the
/// request's HTTP actor, so its <c>audit.view</c> check applies, and it records the export in the audit log. UTC times,
/// formula-safe cells, never cached; when the row cap cut the file short, <c>X-Export-Truncated: true</c> says so.
/// </summary>
public static class AuditExportEndpoints
{
    public const string Route = "/exports/audit.csv";

    public static IEndpointRouteBuilder MapAuditExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, ExportAsync)
            .WithName("ExportAuditCsv")
            .RequireAuthorization(Permissions.AuditView)
            .DisableCookieRedirect();
        return endpoints;
    }

    internal static async Task<IResult> ExportAsync(HttpContext http,
        [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser, IServiceProvider services, CancellationToken ct)
    {
        var query = AuditText.Parse(key => http.Request.Query[key].FirstOrDefault(), AuditQuery.MaxPageSize);
        var audit = ActivatorUtilities.CreateInstance<AuditLogService>(services, currentUser);
        var result = await audit.ExportAsync(query, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.Kind == ErrorKind.Forbidden
                ? TypedResults.StatusCode(StatusCodes.Status403Forbidden)
                : TypedResults.BadRequest();
        }

        var export = result.Value!;
        http.Response.Headers.CacheControl = "no-store";
        if (export.Truncated)
        {
            http.Response.Headers["X-Export-Truncated"] = "true";
        }
        return TypedResults.File(export.Content, "text/csv; charset=utf-8", export.FileName);
    }
}
