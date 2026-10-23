using Microsoft.Extensions.Logging;

namespace CaseIntake.VeteranHistory.Mock;

/// <summary>
/// Mock log events (EventIds 4060-4069 of the E4 range). Only the outcome and duration are logged, inside the request's
/// operation scope; never the SSN, a hash of it, or the returned service-history values.
/// </summary>
internal static partial class HistoryMockLog
{
    [LoggerMessage(EventId = 4060, EventName = "ServiceHistoryServed", Level = LogLevel.Information,
        Message = "Service-history lookup answered {Outcome} in {DurationMs} ms")]
    public static partial void Served(ILogger logger, string outcome, double durationMs);
}
