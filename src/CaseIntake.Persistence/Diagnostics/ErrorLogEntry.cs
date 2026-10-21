using System.Security.Cryptography;
using System.Text;
using CaseIntake.Contracts.Diagnostics;

namespace CaseIntake.Persistence.Diagnostics;

/// <summary>
/// One stored Error or Critical log event (ADR 0015). The captured fields never change; only the handling status, who
/// changed it, when and a note do. <see cref="Fingerprint"/> groups occurrences of the same error: category, event,
/// exception type and the first stack frame.
/// </summary>
public sealed class ErrorLogEntry
{
    public const int CategoryMaxLength = 256, MessageMaxLength = 4000, StackTraceMaxLength = 16000, ShortMaxLength = 256;

    private ErrorLogEntry()
    {
    }

    public long Id { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public ErrorLevel Level { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public int EventId { get; private set; }
    public string? EventName { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string? ExceptionType { get; private set; }
    public string? StackTrace { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? RequestPath { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;

    public ErrorStatus Status { get; set; }
    public DateTimeOffset? StatusChangedUtc { get; set; }

    /// <summary>The user name of whoever last changed the status.</summary>
    public string? StatusChangedBy { get; set; }
    public string? StatusNote { get; set; }

    /// <summary>PostgreSQL row version (<c>xmin</c>), for optimistic concurrency on status changes.</summary>
    public uint Version { get; private set; }

    public static ErrorLogEntry From(CapturedError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ErrorLogEntry
        {
            OccurredAtUtc = error.OccurredAtUtc.ToUniversalTime(),
            Level = error.Level,
            Category = Cap(error.Category, CategoryMaxLength)!,
            EventId = error.EventId,
            EventName = Cap(error.EventName, ShortMaxLength),
            Message = Cap(error.Message, MessageMaxLength)!,
            ExceptionType = Cap(error.ExceptionType, ShortMaxLength),
            StackTrace = Cap(error.StackTrace, StackTraceMaxLength),
            CorrelationId = Cap(error.CorrelationId, 64),
            RequestPath = Cap(error.RequestPath, ShortMaxLength),
            Fingerprint = FingerprintOf(error),
            Status = ErrorStatus.Open,
        };
    }

    /// <summary>16 hex characters of SHA-256 over category, event ID, exception type and the first "at" frame.</summary>
    public static string FingerprintOf(CapturedError error)
    {
        var firstFrame = error.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("at ", StringComparison.Ordinal));
        var key = $"{error.Category}|{error.EventId}|{error.ExceptionType}|{firstFrame}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
    }

    private static string? Cap(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
