using System.Globalization;
using System.Text;
using CaseIntake.Cases;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Web.Components.Pages.Cases;
using CaseIntake.Web.Services;

namespace CaseIntake.Web.Endpoints.Exports;

/// <summary>
/// CSV export of the case list (Reports and the list command bar). The case service is bound to the request's HTTP
/// actor (the circuit adapter does not work outside a circuit), so its <c>cases.view</c> check applies as everywhere
/// else. Filters and sort use the list page's query-string format. Cells are protected against spreadsheet formula
/// injection, and the response is never cached.
/// </summary>
public static partial class CaseExportEndpoints
{
    public const string Route = "/exports/cases.csv";
    private const int MaxRows = 10_000;

    public static IEndpointRouteBuilder MapCaseExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, ExportAsync)
            .WithName("ExportCasesCsv")
            .RequireAuthorization(Permissions.CasesView)
            .DisableCookieRedirect();
        return endpoints;
    }

    internal static async Task<IResult> ExportAsync(HttpContext http,
        [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser, IServiceProvider services,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var query = http.Request.Query;
        // Same filters and sort as the list page (paging is ignored: the export holds every matching case).
        var state = CaseListState.Parse(key => key is "page" or "size" ? [] : [.. query[key].OfType<string>()]);
        var cases = ActivatorUtilities.CreateInstance<CaseService>(services, currentUser);

        var csv = new StringBuilder();
        csv.AppendLine("Id,Title,Status,Priority,Category,Owner,DueOn,CreatedAtUtc,UpdatedAtUtc");
        var page = 1;
        var rows = 0;
        while (rows < MaxRows)
        {
            var result = await cases.ListAsync(state.ToQuery() with { Page = page, PageSize = CaseService.MaxPageSize }, ct);
            if (!result.IsSuccess)
            {
                return result.Error!.Kind == ErrorKind.Forbidden
                    ? TypedResults.StatusCode(StatusCodes.Status403Forbidden)
                    : TypedResults.BadRequest();
            }
            foreach (var c in result.Value!.Items)
            {
                csv.Append(c.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(Cell(c.Title)).Append(',')
                   .Append(UiText.Status(c.Status)).Append(',')
                   .Append(UiText.Priority(c.Priority)).Append(',')
                   .Append(Cell(UiText.Category(c.Category))).Append(',')
                   .Append(c.AssignedTo is null ? string.Empty : Cell(c.AssignedTo)).Append(',')
                   .Append(c.DueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                   .Append(UiText.IsoUtc(c.CreatedAtUtc)).Append(',')
                   .Append(UiText.IsoUtc(c.UpdatedAtUtc)).AppendLine();
                rows++;
            }
            if (result.Value.Items.Count < CaseService.MaxPageSize || rows >= result.Value.TotalCount)
            {
                break;
            }
            page++;
        }

        var actorId = (await currentUser.GetAsync(ct)).UserId;
        Exported(loggers.CreateLogger(typeof(CaseExportEndpoints).FullName!), rows, actorId);
        http.Response.Headers.CacheControl = "no-store";
        // UTF-8 with a byte order mark so spreadsheet programs detect the encoding.
        return TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "cases.csv");
    }

    /// <summary>Quotes a cell and neutralizes leading formula characters (=, +, -, @, tab, carriage return).</summary>
    internal static string Cell(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    [LoggerMessage(EventId = 5101, EventName = "CasesExported", Level = LogLevel.Information,
        Message = "{Rows} cases exported to CSV by {ActorId}")]
    private static partial void Exported(ILogger logger, int rows, string? actorId);
}
