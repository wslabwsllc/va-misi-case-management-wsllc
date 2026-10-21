namespace CaseIntake.Domain.Cases;

/// <summary>Audit action names. Stored as text; never rename an existing value.</summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Edited = "Edited";
    public const string StatusChanged = "StatusChanged";
    public const string Reopened = "Reopened";
    public const string AttachmentUploaded = "AttachmentUploaded";
    public const string Assigned = "Assigned";
}

/// <summary>
/// What happened to a case, independent of who did it (the actor is supplied by the persistence audit helper).
/// <paramref name="Details"/> is a short, safe summary such as changed field names; it never contains an SSN,
/// veteran details or description text.
/// </summary>
public sealed record AuditEvent(
    string Action,
    DateTimeOffset OccurredAtUtc,
    CaseStatus? FromStatus = null,
    CaseStatus? ToStatus = null,
    string? Details = null)
{
    public static AuditEvent Created(DateTimeOffset occurredAtUtc, bool hasVeteranSnapshot) =>
        new(AuditActions.Created, occurredAtUtc, null, CaseWorkflow.InitialStatus,
            hasVeteranSnapshot ? "Veteran snapshot recorded" : null);

    public static AuditEvent Edited(DateTimeOffset occurredAtUtc, IReadOnlyCollection<string> changedFields) =>
        new(AuditActions.Edited, occurredAtUtc, Details: $"Changed: {string.Join(", ", changedFields)}");

    /// <summary>
    /// An attachment was stored (E4-S5). Details hold the attachment ID, size and type, never the file name: a
    /// user-chosen name could contain an SSN-shaped value (decision D8).
    /// </summary>
    public static AuditEvent AttachmentUploaded(DateTimeOffset occurredAtUtc, Guid attachmentId, long sizeBytes, string contentType) =>
        new(AuditActions.AttachmentUploaded, occurredAtUtc,
            Details: string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Attachment {attachmentId:D} ({sizeBytes} bytes, {contentType})"));

    /// <summary>
    /// The owner changed. Details name the new and the previous owner (user names of staff accounts, never case data):
    /// "Assigned to X (was Y)", "Assigned to X" or "Unassigned (was Y)".
    /// </summary>
    public static AuditEvent Assigned(DateTimeOffset occurredAtUtc, string? from, string? to) =>
        new(AuditActions.Assigned, occurredAtUtc, Details: (to, from) switch
        {
            (null, { } previous) => $"Unassigned (was {previous})",
            ({ } owner, null) => $"Assigned to {owner}",
            ({ } owner, { } previous) => $"Assigned to {owner} (was {previous})",
            _ => "Unassigned",
        });

    /// <summary>Resolved→InProgress is recorded as <see cref="AuditActions.Reopened"/>; every other edge as <see cref="AuditActions.StatusChanged"/>.</summary>
    public static AuditEvent StatusChange(DateTimeOffset occurredAtUtc, CaseStatus from, CaseStatus to) =>
        new(CaseWorkflow.IsReopen(from, to) ? AuditActions.Reopened : AuditActions.StatusChanged, occurredAtUtc, from, to);
}

/// <summary>
/// One append-only audit trail row. Created only through <see cref="Record"/>; the persistence layer rejects any
/// attempt to modify or delete a saved entry.
/// </summary>
public sealed class CaseAuditEntry
{
    private CaseAuditEntry()
    {
    }

    public long Id { get; private set; }
    public long CaseId { get; private set; }
    public Case Case { get; private set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Identity user ID of the actor.</summary>
    public string ActorId { get; private set; } = string.Empty;

    /// <summary>Actor's display (user) name at the time of the action.</summary>
    public string Actor { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;
    public CaseStatus? FromStatus { get; private set; }
    public CaseStatus? ToStatus { get; private set; }
    public string? Details { get; private set; }

    /// <summary>The operation ID of the request or action that made the change (also in the logs); null for older rows.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Creates an entry linked to <paramref name="caseEntity"/> through the navigation, so it also works before the case has an ID.</summary>
    public static CaseAuditEntry Record(Case caseEntity, string actorId, string actorName, AuditEvent auditEvent, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(caseEntity);
        ArgumentNullException.ThrowIfNull(auditEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditEvent.Action);
        if (auditEvent.Action.Length > CaseFieldLimits.AuditActionMaxLength)
        {
            throw new ArgumentException("Audit action is too long.", nameof(auditEvent));
        }
        if (auditEvent.Details is { Length: > CaseFieldLimits.AuditDetailsMaxLength })
        {
            throw new ArgumentException("Audit details are too long.", nameof(auditEvent));
        }

        return new CaseAuditEntry
        {
            Case = caseEntity,
            CaseId = caseEntity.Id,
            OccurredAtUtc = auditEvent.OccurredAtUtc.ToUniversalTime(),
            ActorId = CaseTextRules.TruncateActor(actorId),
            Actor = CaseTextRules.TruncateActor(actorName),
            Action = auditEvent.Action,
            FromStatus = auditEvent.FromStatus,
            ToStatus = auditEvent.ToStatus,
            Details = auditEvent.Details,
            CorrelationId = correlationId is { Length: > 64 } ? correlationId[..64] : correlationId,
        };
    }
}
