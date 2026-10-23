namespace CaseIntake.Domain.Cases;

/// <summary>Veteran details captured from the lookup at intake. Only the last four SSN digits are ever held (A8, OQ-5b).</summary>
public sealed record CaseVeteran(string Name, string Address, string SsnLast4,
    ServiceHistoryStatus? HistoryStatus = null, ServiceHistory? History = null);

/// <summary>Result of the service-history lookup taken at intake. Null on a case means it predates Change 2 (D3).</summary>
public enum ServiceHistoryStatus { Found, NotFound, Unavailable }

/// <summary>Service history from the Veteran Service History service, its authoritative source (R-CR2-4). Branch and
/// character of service are free text (D9); the end and last-verified dates are optional (D7).</summary>
public sealed record ServiceHistory(string Branch, DateOnly ServiceStartDate, DateOnly? ServiceEndDate,
    string CharacterOfService, DateOnly? LastVerifiedOn);

/// <summary>A domain rule was violated. Services check the rules first and return a result; this is the last line of defense.</summary>
public sealed class CaseRuleException(string code, string message, IReadOnlyList<FieldError>? fieldErrors = null)
    : InvalidOperationException(message)
{
    public const string ValidationCode = "Validation";
    public const string CaseClosedCode = "CaseClosed";
    public const string InvalidTransitionCode = "InvalidTransition";

    public string Code { get; } = code;
    public IReadOnlyList<FieldError> FieldErrors { get; } = fieldErrors ?? [];
}

/// <summary>
/// A case. The ID is assigned by the database and never changes; status changes only through
/// <see cref="TransitionTo"/>; audit fields are refreshed by every mutation. <see cref="Version"/> is the optimistic
/// concurrency token maintained by the database.
/// </summary>
public sealed class Case
{
    private Case()
    {
    }

    public long Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public CaseStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>); changes on every committed update.</summary>
    public uint Version { get; private set; }

    public string? VeteranName { get; private set; }
    public string? VeteranAddress { get; private set; }
    public string? VeteranSsnLast4 { get; private set; }

    /// <summary>Stable fixture key for seeded demo cases; null for cases entered by users.</summary>
    public string? SeedKey { get; private set; }

    public CasePriority Priority { get; private set; } = CasePriority.Medium;
    public CaseCategory Category { get; private set; } = CaseCategory.General;

    /// <summary>The owner's user name; null when the case is unassigned.</summary>
    public string? AssignedTo { get; private set; }

    /// <summary>The UTC calendar date the case should be resolved by; null when it has none (never overdue).</summary>
    public DateOnly? DueOn { get; private set; }

    public CaseVeteran? Veteran => VeteranName is null || VeteranAddress is null || VeteranSsnLast4 is null
        ? null
        : new CaseVeteran(VeteranName, VeteranAddress, VeteranSsnLast4);

    public CaseTriage Triage => new(Priority, Category, AssignedTo, DueOn);

    public bool IsClosed => Status == CaseStatus.Closed;

    /// <summary>
    /// Creates a new case in the initial status. The title is normalized with <see cref="CaseTextRules"/>. Without
    /// <paramref name="triage"/> the case is Medium priority, General, unassigned and has no due date.
    /// </summary>
    /// <exception cref="CaseRuleException">The text, veteran snapshot or triage fields are invalid.</exception>
    public static Case Create(string title, string? description, CaseVeteran? veteran, string actor,
        DateTimeOffset nowUtc, string? seedKey = null, CaseTriage? triage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var normalizedTitle = CaseTextRules.NormalizeTitle(title);
        var normalizedDescription = CaseTextRules.NormalizeDescription(description);
        var normalizedTriage = Normalize(triage ?? CaseTriage.Default);
        List<FieldError> errors = [.. CaseTextRules.ValidateCase(normalizedTitle, normalizedDescription)];
        if (veteran is not null)
        {
            errors.AddRange(CaseTextRules.ValidateVeteran(veteran.Name, veteran.Address, veteran.SsnLast4));
        }
        errors.AddRange(CaseTriageRules.Validate(normalizedTriage));
        ThrowIfInvalid(errors);

        var now = nowUtc.ToUniversalTime();
        return new Case
        {
            Title = normalizedTitle,
            Description = normalizedDescription,
            Status = CaseWorkflow.InitialStatus,
            CreatedAtUtc = now,
            CreatedBy = CaseTextRules.TruncateActor(actor),
            UpdatedAtUtc = now,
            UpdatedBy = CaseTextRules.TruncateActor(actor),
            VeteranName = veteran?.Name,
            VeteranAddress = veteran?.Address,
            VeteranSsnLast4 = veteran?.SsnLast4,
            SeedKey = seedKey,
            Priority = normalizedTriage.Priority,
            Category = normalizedTriage.Category,
            AssignedTo = normalizedTriage.AssignedTo,
            DueOn = normalizedTriage.DueOn,
        };
    }

    /// <summary>
    /// Names of the fields that <see cref="Edit"/> would change, in a fixed order (Title, Description, Priority,
    /// Category, AssignedTo, DueOn). Comparison is ordinal after the same normalization the edit applies; a null
    /// <paramref name="triage"/> leaves the triage fields out of the comparison.
    /// </summary>
    public IReadOnlyList<string> DetectChanges(string title, string? description, CaseTriage? triage = null)
    {
        List<string> changed = [];
        if (!string.Equals(Title, CaseTextRules.NormalizeTitle(title), StringComparison.Ordinal))
        {
            changed.Add(CaseFieldNames.Title);
        }
        if (!string.Equals(Description, CaseTextRules.NormalizeDescription(description), StringComparison.Ordinal))
        {
            changed.Add(CaseFieldNames.Description);
        }
        if (triage is not null)
        {
            var t = Normalize(triage);
            if (Priority != t.Priority)
            {
                changed.Add(CaseFieldNames.Priority);
            }
            if (Category != t.Category)
            {
                changed.Add(CaseFieldNames.Category);
            }
            if (!string.Equals(AssignedTo, t.AssignedTo, StringComparison.Ordinal))
            {
                changed.Add(CaseFieldNames.AssignedTo);
            }
            if (DueOn != t.DueOn)
            {
                changed.Add(CaseFieldNames.DueOn);
            }
        }

        return changed;
    }

    /// <summary>
    /// Applies an edit of the text and, when <paramref name="triage"/> is given, the priority, category and due date (the
    /// owner in <paramref name="triage"/> is ignored: it changes only through <see cref="Assign"/>). Returns the changed
    /// field names; when nothing changed the case is left untouched (no timestamp or actor update) and an empty list is
    /// returned.
    /// </summary>
    /// <exception cref="CaseRuleException">The case is closed, or the text or triage fields are invalid.</exception>
    public IReadOnlyList<string> Edit(string title, string? description, string actor, DateTimeOffset nowUtc, CaseTriage? triage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (IsClosed)
        {
            throw new CaseRuleException(CaseRuleException.CaseClosedCode, "Closed cases cannot be edited.");
        }

        var normalizedTitle = CaseTextRules.NormalizeTitle(title);
        var normalizedDescription = CaseTextRules.NormalizeDescription(description);
        // The owner changes only through Assign (its own rules and audit entry); an edit keeps the current owner.
        var normalizedTriage = triage is null ? null : Normalize(triage) with { AssignedTo = AssignedTo };
        List<FieldError> errors = [.. CaseTextRules.ValidateCase(normalizedTitle, normalizedDescription)];
        if (normalizedTriage is not null)
        {
            errors.AddRange(CaseTriageRules.Validate(normalizedTriage));
        }
        ThrowIfInvalid(errors);

        var changed = DetectChanges(normalizedTitle, normalizedDescription, normalizedTriage);
        if (changed.Count == 0)
        {
            return changed;
        }

        Title = normalizedTitle;
        Description = normalizedDescription;
        if (normalizedTriage is not null)
        {
            Priority = normalizedTriage.Priority;
            Category = normalizedTriage.Category;
            AssignedTo = normalizedTriage.AssignedTo;
            DueOn = normalizedTriage.DueOn;
        }
        Touch(actor, nowUtc);
        return changed;
    }

    /// <summary>
    /// Sets the owner (null or empty = unassigned). Returns whether it changed (when not, the case is untouched) and the
    /// previous owner. Who may assign whom is decided by the caller (CaseAssignmentRules).
    /// </summary>
    /// <exception cref="CaseRuleException">The case is closed, or the owner is invalid.</exception>
    public (bool Changed, string? Previous) Assign(string? owner, string actor, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (IsClosed)
        {
            throw new CaseRuleException(CaseRuleException.CaseClosedCode, "Closed cases cannot be reassigned.");
        }
        var normalized = CaseTriageRules.NormalizeAssignee(owner);
        ThrowIfInvalid(CaseTriageRules.Validate(Triage with { AssignedTo = normalized }));
        if (string.Equals(AssignedTo, normalized, StringComparison.Ordinal))
        {
            return (false, AssignedTo);
        }
        var previous = AssignedTo;
        AssignedTo = normalized;
        Touch(actor, nowUtc);
        return (true, previous);
    }

    private static CaseTriage Normalize(CaseTriage triage) =>
        triage with { AssignedTo = CaseTriageRules.NormalizeAssignee(triage.AssignedTo) };

    /// <summary>Moves the case along an allowed workflow edge and returns the previous status.</summary>
    /// <exception cref="CaseRuleException">The workflow does not allow the transition.</exception>
    public CaseStatus TransitionTo(CaseStatus target, ICaseWorkflow workflow, string actor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (!workflow.IsAllowed(Status, target))
        {
            throw new CaseRuleException(CaseRuleException.InvalidTransitionCode,
                $"A case cannot move from {Status} to {target}.");
        }

        var from = Status;
        Status = target;
        Touch(actor, nowUtc);
        return from;
    }

    private void Touch(string actor, DateTimeOffset nowUtc)
    {
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        UpdatedBy = CaseTextRules.TruncateActor(actor);
    }

    private static void ThrowIfInvalid(IReadOnlyList<FieldError> errors)
    {
        if (errors.Count > 0)
        {
            throw new CaseRuleException(CaseRuleException.ValidationCode, "The case data is invalid.", errors);
        }
    }
}
