using CaseIntake.Contracts.Security;
using Microsoft.Extensions.Logging;

namespace CaseIntake.VeteranLookup.Client;

/// <summary>
/// Client log events (EventIds 4001-4009 of the E4 range, plus the shared AuthorizationDenied event). Only the
/// outcome, duration, HTTP status and exception type are logged; never the SSN, a hash of it, or veteran details.
/// </summary>
internal static partial class LookupLog
{
    [LoggerMessage(EventId = 4001, EventName = "VeteranLookupCompleted", Level = LogLevel.Information,
        Message = "Veteran lookup completed with {Outcome} in {DurationMs} ms")]
    public static partial void Completed(ILogger logger, string outcome, double durationMs);

    [LoggerMessage(EventId = 4002, EventName = "VeteranLookupUnavailable", Level = LogLevel.Warning,
        Message = "Veteran lookup unavailable ({Reason}, HTTP {StatusCode}) after {DurationMs} ms")]
    public static partial void Unavailable(ILogger logger, string reason, int? statusCode, double durationMs);

    [LoggerMessage(EventId = 4003, EventName = "VeteranLookupRejected", Level = LogLevel.Information,
        Message = "Veteran lookup rejected locally: the SSN is not in an accepted format")]
    public static partial void Rejected(ILogger logger);

    [LoggerMessage(EventId = 4004, EventName = "VeteranLookupRetrying", Level = LogLevel.Information,
        Message = "Veteran lookup attempt {Attempt} after {Reason} (HTTP {StatusCode})")]
    public static partial void Retrying(ILogger logger, int attempt, string reason, int? statusCode);

    [LoggerMessage(EventId = SecurityEvents.AuthorizationDeniedId, EventName = SecurityEvents.AuthorizationDeniedName,
        Level = LogLevel.Warning, Message = "Authorization denied for {UserId}: {Permission} on {ResourceId} ({Operation})")]
    public static partial void AuthorizationDenied(ILogger logger, string? userId, string permission, string? resourceId, string operation);
}
