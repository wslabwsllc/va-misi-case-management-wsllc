using CaseIntake.Contracts.Security;
using Microsoft.Extensions.Logging;

namespace CaseIntake.TestResults;

/// <summary>
/// Test results log events (EventIds 5200-5299 of the E5 range, plus the shared AuthorizationDenied event). Only run
/// keys, counts, IDs and exception type names are logged; never test output, file paths or exception messages.
/// </summary>
internal static partial class TestResultsLog
{
    [LoggerMessage(EventId = 5201, EventName = "TestRunImported", Level = LogLevel.Information,
        Message = "Test run {RunKey} imported: {Total} results, {Failed} failed, {NotExecuted} not executed, {Artifacts} evidence files")]
    public static partial void RunImported(ILogger logger, string runKey, int total, int failed, int notExecuted, int artifacts);

    [LoggerMessage(EventId = 5202, EventName = "TestRunImportFailed", Level = LogLevel.Warning,
        Message = "Test run folder {RunKey} could not be imported ({ExceptionType})")]
    public static partial void RunImportFailed(ILogger logger, string runKey, string exceptionType);

    [LoggerMessage(EventId = 5203, EventName = "TestEvidenceAccessed", Level = LogLevel.Information,
        Message = "Test evidence {ArtifactId} ({Kind}) {Access} by {ActorId}")]
    public static partial void EvidenceAccessed(ILogger logger, Guid artifactId, string kind, string access, string? actorId);

    [LoggerMessage(EventId = 5204, EventName = "TestReportExported", Level = LogLevel.Information,
        Message = "Test run {RunId} exported as {Format} by {ActorId}")]
    public static partial void ReportExported(ILogger logger, long runId, string format, string? actorId);

    [LoggerMessage(EventId = 5205, EventName = "TestArtifactsPurged", Level = LogLevel.Information,
        Message = "Retention removed {Files} evidence files of {Runs} runs")]
    public static partial void ArtifactsPurged(ILogger logger, int files, int runs);

    [LoggerMessage(EventId = 5206, EventName = "TestEvidenceMissing", Level = LogLevel.Warning,
        Message = "Test evidence {ArtifactId} is missing from the results store or outside it")]
    public static partial void EvidenceMissing(ILogger logger, Guid artifactId);

    [LoggerMessage(EventId = 5207, EventName = "TestResultsBackgroundFailed", Level = LogLevel.Warning,
        Message = "Background test results import or retention failed ({ExceptionType})")]
    public static partial void BackgroundFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = SecurityEvents.AuthorizationDeniedId, EventName = SecurityEvents.AuthorizationDeniedName,
        Level = LogLevel.Warning, Message = "Authorization denied for {UserId}: {Permission} on {ResourceId}")]
    public static partial void AuthorizationDenied(ILogger logger, string? userId, string permission, string? resourceId);
}
