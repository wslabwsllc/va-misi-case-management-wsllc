using CaseIntake.Contracts.Security;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Cases;

/// <summary>
/// E2 log events (EventId range 2000-2999, plus the shared E3 AuthorizationDenied event). Only safe fields are logged:
/// case IDs, user IDs, statuses, field names and error codes. Titles, descriptions and veteran details are never logged.
/// </summary>
internal static partial class CaseLog
{
    [LoggerMessage(EventId = 2001, EventName = "CaseCreated", Level = LogLevel.Information,
        Message = "Case {CaseId} created by {ActorId}")]
    public static partial void CaseCreated(ILogger logger, long caseId, string actorId);

    [LoggerMessage(EventId = 2002, EventName = "CaseUpdated", Level = LogLevel.Information,
        Message = "Case {CaseId} edited by {ActorId}; changed {ChangedFields}")]
    public static partial void CaseUpdated(ILogger logger, long caseId, string actorId, string changedFields);

    [LoggerMessage(EventId = 2003, EventName = "CaseStatusChanged", Level = LogLevel.Information,
        Message = "Case {CaseId} moved from {FromStatus} to {ToStatus} by {ActorId}")]
    public static partial void CaseStatusChanged(ILogger logger, long caseId, string fromStatus, string toStatus, string actorId);

    /// <summary>The shared E3-S5 <c>AuthorizationDenied</c> event (user, permission, resource), plus the operation name.</summary>
    [LoggerMessage(EventId = SecurityEvents.AuthorizationDeniedId, EventName = SecurityEvents.AuthorizationDeniedName,
        Level = LogLevel.Warning, Message = "Authorization denied for {UserId}: {Permission} on {ResourceId} ({Operation})")]
    public static partial void AuthorizationDenied(ILogger logger, string? userId, string permission, string? resourceId, string operation);

    [LoggerMessage(EventId = 2005, EventName = "CaseVersionConflict", Level = LogLevel.Information,
        Message = "Case {CaseId} {Operation} rejected: stale version")]
    public static partial void VersionConflict(ILogger logger, long caseId, string operation);

    /// <summary>The owner changed. User names are not logged; the audit trail records them.</summary>
    [LoggerMessage(EventId = 2004, EventName = "CaseAssigned", Level = LogLevel.Information,
        Message = "Case {CaseId} owner changed by {ActorId}")]
    public static partial void CaseAssigned(ILogger logger, long caseId, string actorId);

    [LoggerMessage(EventId = 2006, EventName = "CaseRequestRejected", Level = LogLevel.Information,
        Message = "Case operation {Operation} rejected with {ErrorCode}")]
    public static partial void RequestRejected(ILogger logger, string operation, string errorCode);

    [LoggerMessage(EventId = 2010, EventName = "CasesSeeded", Level = LogLevel.Information,
        Message = "Case seed added {AddedCount} of {FixtureCount} fixture cases")]
    public static partial void CasesSeeded(ILogger logger, int addedCount, int fixtureCount);
}
