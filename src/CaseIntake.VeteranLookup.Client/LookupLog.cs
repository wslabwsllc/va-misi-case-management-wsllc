using CaseIntake.Contracts.Security;
using Microsoft.Extensions.Logging;

namespace CaseIntake.VeteranLookup.Client;

/// <summary>
/// Client log events (EventIds 4001-4009 for the Lookup and 4010-4013 for Service History, plus the shared
/// AuthorizationDenied event). Only the outcome, duration, HTTP status and exception type are logged; never the SSN, a
/// hash of it, veteran details or service-history values.
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

    // Service History client (Change 2, EventIds 4010-4013): the same safe fields as the Lookup events above.

    [LoggerMessage(EventId = 4010, EventName = "ServiceHistoryCompleted", Level = LogLevel.Information,
        Message = "Service-history lookup completed with {Outcome} in {DurationMs} ms")]
    public static partial void HistoryCompleted(ILogger logger, string outcome, double durationMs);

    [LoggerMessage(EventId = 4011, EventName = "ServiceHistoryUnavailable", Level = LogLevel.Warning,
        Message = "Service-history lookup unavailable ({Reason}, HTTP {StatusCode}) after {DurationMs} ms")]
    public static partial void HistoryUnavailable(ILogger logger, string reason, int? statusCode, double durationMs);

    [LoggerMessage(EventId = 4012, EventName = "ServiceHistoryRejected", Level = LogLevel.Information,
        Message = "Service-history lookup rejected locally: the SSN is not in an accepted format")]
    public static partial void HistoryRejected(ILogger logger);

    [LoggerMessage(EventId = 4013, EventName = "ServiceHistoryRetrying", Level = LogLevel.Information,
        Message = "Service-history lookup attempt {Attempt} after {Reason} (HTTP {StatusCode})")]
    public static partial void HistoryRetrying(ILogger logger, int attempt, string reason, int? statusCode);

    [LoggerMessage(EventId = SecurityEvents.AuthorizationDeniedId, EventName = SecurityEvents.AuthorizationDeniedName,
        Level = LogLevel.Warning, Message = "Authorization denied for {UserId}: {Permission} on {ResourceId} ({Operation})")]
    public static partial void AuthorizationDenied(ILogger logger, string? userId, string permission, string? resourceId, string operation);
}
