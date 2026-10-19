using Microsoft.Extensions.Logging;

namespace CaseIntake.VeteranLookup.Mock;

/// <summary>
/// Mock log events (EventIds 4050-4059 of the E4 range). Only the outcome and duration are logged, inside an
/// operation scope with a random operation ID; never the SSN, a hash of it, or the returned name or address.
/// </summary>
internal static partial class MockLog
{
    [LoggerMessage(EventId = 4050, EventName = "VeteranLookupServed", Level = LogLevel.Information,
        Message = "Veteran lookup answered {Outcome} in {DurationMs} ms")]
    public static partial void LookupServed(ILogger logger, string outcome, double durationMs);
}
