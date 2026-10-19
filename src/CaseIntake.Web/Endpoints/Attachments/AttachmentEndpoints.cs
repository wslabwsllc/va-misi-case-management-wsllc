using CaseIntake.Attachments;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.ServiceDefaults;

namespace CaseIntake.Web.Endpoints.Attachments;

/// <summary>
/// Attachment download (E4-S6), owned by E4 inside the web composition root so it may use both the Identity HTTP
/// actor adapter and the attachment module without one feature module referencing another.
/// <para>
/// Protection is layered: the endpoint policy requires <c>attachments.download</c> (401 anonymous, 403 without it,
/// never a login redirect); the service checks the same permission again for the HTTP actor and only returns an
/// attachment that belongs to the case in the URL (otherwise 404, identical to an unknown ID). Content is read from
/// storage outside wwwroot and always sent as a download with the allowlisted type, <c>nosniff</c> (set globally)
/// and <c>no-store</c>, so a browser never renders or caches it.
/// </para>
/// </summary>
public static partial class AttachmentEndpoints
{
    public const string ContentRoute = "/cases/{caseId:long}/attachments/{id:guid}/content";

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ContentRoute, DownloadAsync)
            .WithName("DownloadAttachment")
            .RequireAuthorization(Permissions.AttachmentsDownload)
            .DisableCookieRedirect();
        return endpoints;
    }

    internal static async Task<IResult> DownloadAsync(long caseId, Guid id,
        [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser,
        IAttachmentServiceFactory attachments, HttpContext http, ILoggerFactory loggers, CancellationToken ct)
    {
        var actorId = (await currentUser.GetAsync(ct)).UserId;
        var result = await attachments.Create(currentUser).OpenReadAsync(caseId, id, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.Kind == ErrorKind.Forbidden
                ? TypedResults.StatusCode(StatusCodes.Status403Forbidden)
                : TypedResults.NotFound();
        }

        var content = result.Value!;
        http.Response.Headers.CacheControl = "no-store";
        var logger = loggers.CreateLogger(typeof(AttachmentEndpoints).FullName!);
        http.Response.OnCompleted(() =>
        {
            // Only a response that was written completely counts as a download; aborted or failed ones do not. The
            // request's logging scope has ended by now, so the user and operation ID are recorded as fields.
            if (!http.RequestAborted.IsCancellationRequested && http.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status206PartialContent)
            {
                Downloaded(logger, id, caseId, actorId, http.Response.StatusCode,
                    http.Response.Headers[OperationContextMiddleware.HeaderName].ToString());
            }
            return Task.CompletedTask;
        });

        // TypedResults.File disposes the stream when the response ends and encodes the name as filename*=UTF-8''...
        return TypedResults.File(content.Content, content.ContentType, content.FileName, enableRangeProcessing: true);
    }

    [LoggerMessage(EventId = 4103, EventName = "AttachmentDownloaded", Level = LogLevel.Information,
        Message = "Attachment {AttachmentId} on case {CaseId} downloaded by {ActorId} (HTTP {StatusCode}, operation {OperationId})")]
    private static partial void Downloaded(ILogger logger, Guid attachmentId, long caseId, string? actorId, int statusCode, string operationId);
}
