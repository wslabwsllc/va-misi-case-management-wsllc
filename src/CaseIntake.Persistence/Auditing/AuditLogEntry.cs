using System.Text.Json;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Common;

namespace CaseIntake.Persistence.Auditing;

/// <summary>
/// One row of the append-only system audit log (ADR 0014): authentication, authorization denials and user and role
/// administration. Case, attachment and test-results events stay in their own trails, which the audit log viewer reads
/// together with this table. Created only through <see cref="From"/>; saved rows cannot be modified or deleted.
/// </summary>
public sealed class AuditLogEntry
{
    public const int ShortTextMaxLength = 256, SummaryMaxLength = 1000, CorrelationIdMaxLength = 64;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private AuditLogEntry()
    {
    }

    public long Id { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Identity user ID of the actor; null when the actor is unknown (a failed sign-in for no account) or anonymous.</summary>
    public string? ActorId { get; private set; }
    public string? ActorName { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public AuditOutcome Outcome { get; private set; }
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? EntityName { get; private set; }
    public string? Summary { get; private set; }

    /// <summary>JSON object of field → value before the change (jsonb); null when nothing existed or nothing is recorded.</summary>
    public string? Before { get; private set; }

    /// <summary>JSON object of field → value after the change (jsonb).</summary>
    public string? After { get; private set; }

    /// <summary>The operation ID that also appears in the logs (<see cref="OperationContext"/>).</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    public static AuditLogEntry From(AuditRecord record, DateTimeOffset occurredAtUtc, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new AuditLogEntry
        {
            OccurredAtUtc = occurredAtUtc.ToUniversalTime(),
            ActorId = Cap(record.ActorId, ShortTextMaxLength),
            ActorName = Cap(record.ActorName, ShortTextMaxLength),
            Category = Cap(record.Category, 64)!,
            Action = Cap(record.Action, 64)!,
            Outcome = record.Outcome,
            EntityType = Cap(record.EntityType, 64),
            EntityId = Cap(record.EntityId, ShortTextMaxLength),
            EntityName = Cap(record.EntityName, ShortTextMaxLength),
            Summary = Cap(record.Summary, SummaryMaxLength),
            Before = Serialize(record.Before),
            After = Serialize(record.After),
            CorrelationId = Cap(correlationId, CorrelationIdMaxLength)!,
        };
    }

    public static IReadOnlyDictionary<string, string?> Deserialize(string? json) =>
        string.IsNullOrEmpty(json)
            ? new Dictionary<string, string?>()
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(json, Json) ?? [];

    private static string? Serialize(IReadOnlyDictionary<string, string?>? values) =>
        values is null || values.Count == 0 ? null : JsonSerializer.Serialize(values, Json);

    private static string? Cap(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
