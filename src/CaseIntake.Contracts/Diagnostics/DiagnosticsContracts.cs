using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;

namespace CaseIntake.Contracts.Diagnostics;

/// <summary>
/// An error or critical log event captured for the error log (ADR 0015). Already safe: the message is the rendered log
/// message with SSN-shaped values redacted, the exception is its type and stack trace only (never its message, which can
/// echo submitted values).
/// </summary>
public sealed record CapturedError(
    DateTimeOffset OccurredAtUtc,
    ErrorLevel Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? ExceptionType,
    string? StackTrace,
    string? CorrelationId,
    string? RequestPath);

/// <summary>Accepts captured errors without blocking the caller; drops them when the queue is full (ADR 0015).</summary>
public interface IErrorLogSink
{
    bool TryEnqueue(CapturedError error);
}

public enum ErrorLevel { Error, Critical }

/// <summary>Where an error is in its handling. New errors are <see cref="Open"/>.</summary>
public enum ErrorStatus { Open, Acknowledged, Resolved }

/// <summary>
/// Error log query; filters combine with AND. Dates are inclusive Eastern calendar days. <paramref name="Search"/> matches
/// part of the message, category, exception type, request path or correlation ID; <paramref name="Fingerprint"/> selects
/// occurrences of the same error.
/// </summary>
public sealed record ErrorQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    ErrorLevel? Level = null,
    ErrorStatus? Status = null,
    string? Search = null,
    string? Fingerprint = null,
    int Page = 1,
    int PageSize = 50)
{
    public const int MaxPageSize = 200;
    public const int SearchMaxLength = 200;
}

public sealed record ErrorSummaryDto(long Id, DateTimeOffset OccurredAtUtc, ErrorLevel Level, string Category, string Message,
    string? ExceptionType, ErrorStatus Status, string? CorrelationId, string Fingerprint);

/// <summary>One error with everything the detail page shows. <see cref="Occurrences"/> counts errors with the same fingerprint.</summary>
public sealed record ErrorDetailDto(
    long Id,
    DateTimeOffset OccurredAtUtc,
    ErrorLevel Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? ExceptionType,
    string? StackTrace,
    string? CorrelationId,
    string? RequestPath,
    string Fingerprint,
    int Occurrences,
    ErrorStatus Status,
    DateTimeOffset? StatusChangedUtc,
    string? StatusChangedBy,
    string? StatusNote,
    long Version);

/// <summary>Moves an error to <paramref name="Status"/>; <paramref name="Version"/> is the detail's version (stale = Conflict).</summary>
public sealed record SetErrorStatusRequest(ErrorStatus Status, string? Note, long Version)
{
    public const int NoteMaxLength = 1000;
}

/// <summary>The stored error log (<c>errors.view</c> to read, <c>errors.manage</c> to change status).</summary>
public interface IErrorLogService
{
    Task<Result<PagedResult<ErrorSummaryDto>>> ListAsync(ErrorQuery query, CancellationToken ct);

    Task<Result<ErrorDetailDto>> GetAsync(long id, CancellationToken ct);

    /// <summary>Finds the error recorded for an operation ID (the reference shown on the error page).</summary>
    Task<Result<IReadOnlyList<ErrorSummaryDto>>> FindByReferenceAsync(string reference, CancellationToken ct);

    Task<Result<ErrorDetailDto>> SetStatusAsync(long id, SetErrorStatusRequest request, CancellationToken ct);
}

public enum HealthState { Healthy, Degraded, Unhealthy }

public sealed record HealthCheckDto(string Name, HealthState State, string? Description, TimeSpan Duration);

public sealed record CountDto(string Label, int Count);

/// <summary>Everything the System Health page shows, gathered at <see cref="CheckedAtUtc"/>.</summary>
public sealed record SystemHealthDto(
    DateTimeOffset CheckedAtUtc,
    HealthState Overall,
    IReadOnlyList<HealthCheckDto> Checks,
    string Version,
    string Environment,
    DateTimeOffset StartedAtUtc,
    bool DatabaseReachable,
    int AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    int OpenErrors,
    IReadOnlyList<CountDto> ErrorsLast24Hours,
    IReadOnlyList<CountDto> SecurityLast24Hours,
    IReadOnlyList<ErrorSummaryDto> LatestErrors);

/// <summary>System Health (<c>system.health</c>): runs the health checks and reads database and log statistics.</summary>
public interface ISystemHealthService
{
    Task<Result<SystemHealthDto>> GetAsync(CancellationToken ct);
}

/// <summary>
/// Marks code that writes the error log, so errors it logs itself (for example the database being down) are not captured
/// again (ADR 0015). Flows with the async call chain.
/// </summary>
public static class ErrorCaptureSuppression
{
    private static readonly AsyncLocal<bool> Active = new();

    public static bool IsActive => Active.Value;

    public static IDisposable Begin()
    {
        var previous = Active.Value;
        Active.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}
