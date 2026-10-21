using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.TestResults;
using CaseIntake.Web.Components.Pages.TestResults;

namespace CaseIntake.Web.Endpoints.TestResults;

/// <summary>
/// Test evidence and run exports (E5 test management), in the web composition root so they can use the Identity HTTP
/// actor adapter. Evidence is never served as a static file: the endpoint policy requires <c>testresults.view</c> (401
/// anonymous, 403 without it, never a login redirect), and the service checks it again, resolves the file inside the
/// results root and audits the access. Responses are <c>no-store</c>. Only images, videos and plain text are shown
/// inline (for the screenshot preview); everything else is sent as a download.
/// </summary>
public static class TestResultEndpoints
{
    public const string ArtifactRoute = "/test-results/artifacts/{id:guid}";
    public const string ExportRoute = "/test-results/runs/{runId:long}/export/{format}";

    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "video/webm", "video/mp4", "text/plain",
    };

    public static IEndpointRouteBuilder MapTestResultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ArtifactRoute, ArtifactAsync)
            .WithName("TestEvidence")
            .RequireAuthorization(Permissions.TestResultsView)
            .DisableCookieRedirect();
        endpoints.MapGet(ExportRoute, ExportAsync)
            .WithName("ExportTestRun")
            .RequireAuthorization(Permissions.TestResultsView)
            .DisableCookieRedirect();
        // Filtered exports (QA 508 enhancement): the same query string and filters as the result and test case lists.
        endpoints.MapGet(TestResultRoutes.ExecutionsExport, ExportExecutionsAsync)
            .WithName("ExportFilteredTestResults")
            .RequireAuthorization(Permissions.TestResultsView)
            .DisableCookieRedirect();
        endpoints.MapGet(TestResultRoutes.TestCasesExport, ExportTestCasesAsync)
            .WithName("ExportFilteredTestCases")
            .RequireAuthorization(Permissions.TestResultsView)
            .DisableCookieRedirect();
        return endpoints;
    }

    internal static async Task<IResult> ArtifactAsync(Guid id, bool? download,
        [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser,
        ITestResultsServiceFactory results, HttpContext http, CancellationToken ct)
    {
        var asDownload = download == true;
        var result = await results.Create(currentUser).OpenArtifactAsync(id, asDownload, ct);
        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        var content = result.Value!;
        http.Response.Headers.CacheControl = "no-store";
        // Any other type (HTML reports, traces, JSON) is sent as opaque bytes, so no browser can ever render it.
        var previewable = InlineTypes.Contains(content.ContentType);
        var contentType = previewable ? content.ContentType : "application/octet-stream";
        // TypedResults.File disposes the stream when the response ends; a file name makes it an attachment.
        return TypedResults.File(content.Content, contentType, previewable && !asDownload ? null : content.FileName, enableRangeProcessing: true);
    }

    internal static async Task<IResult> ExportAsync(long runId, string format,
        [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser,
        ITestResultsServiceFactory results, HttpContext http, CancellationToken ct)
    {
        var result = await results.Create(currentUser).ExportRunAsync(runId, format.ToLowerInvariant(), ct);
        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        http.Response.Headers.CacheControl = "no-store";
        var file = result.Value!;
        return TypedResults.File(file.Content, file.ContentType, file.FileName);
    }

    internal static async Task<IResult> ExportExecutionsAsync(
        [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser,
        ITestResultsServiceFactory results, HttpContext http, CancellationToken ct)
    {
        var q = http.Request.Query;
        long? runId = long.TryParse(q["run"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;
        var query = TestResultsQueryString.Executions(runId, key => q[key].FirstOrDefault());
        return File(http, await results.Create(currentUser).ExportExecutionsAsync(query, ct));
    }

    internal static async Task<IResult> ExportTestCasesAsync(
        [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser,
        ITestResultsServiceFactory results, HttpContext http, CancellationToken ct)
    {
        var q = http.Request.Query;
        var query = TestResultsQueryString.Cases(key => q[key].FirstOrDefault());
        return File(http, await results.Create(currentUser).ExportTestCasesAsync(query, ct));
    }

    private static IResult File(HttpContext http, Result<TestReportFile> result)
    {
        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }
        http.Response.Headers.CacheControl = "no-store";
        var file = result.Value!;
        return TypedResults.File(file.Content, file.ContentType, file.FileName);
    }

    private static IResult Failure(Error error) => error.Kind switch
    {
        ErrorKind.Forbidden => TypedResults.StatusCode(StatusCodes.Status403Forbidden),
        ErrorKind.Validation => TypedResults.BadRequest(),
        _ => TypedResults.NotFound(),
    };
}
