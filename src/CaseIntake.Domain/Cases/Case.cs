namespace CaseIntake.Domain.Cases;

/// <summary>Veteran details captured from the lookup at intake. Only the last four SSN digits are ever held (A8, OQ-5b).</summary>
public sealed record CaseVeteran(string Name, string Address, string SsnLast4);

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

    public CaseVeteran? Veteran => VeteranName is null || VeteranAddress is null || VeteranSsnLast4 is null
        ? null
        : new CaseVeteran(VeteranName, VeteranAddress, VeteranSsnLast4);

    public bool IsClosed => Status == CaseStatus.Closed;

    /// <summary>Creates a new case in the initial status. The title is normalized with <see cref="CaseTextRules"/>.</summary>
    /// <exception cref="CaseRuleException">The text or veteran snapshot violates <see cref="CaseTextRules"/>.</exception>
    public static Case Create(string title, string? description, CaseVeteran? veteran, string actor,
        DateTimeOffset nowUtc, string? seedKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var normalizedTitle = CaseTextRules.NormalizeTitle(title);
        var normalizedDescription = CaseTextRules.NormalizeDescription(description);
        List<FieldError> errors = [.. CaseTextRules.ValidateCase(normalizedTitle, normalizedDescription)];
        if (veteran is not null)
        {
            errors.AddRange(CaseTextRules.ValidateVeteran(veteran.Name, veteran.Address, veteran.SsnLast4));
        }
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
        };
    }

    /// <summary>
    /// Names of the fields that <see cref="Edit"/> would change, in a fixed order (Title, Description). Comparison is
    /// ordinal after the same normalization the edit applies.
    /// </summary>
    public IReadOnlyList<string> DetectChanges(string title, string? description)
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

        return changed;
    }

    /// <summary>
    /// Applies a title/description edit. Returns the changed field names; when nothing changed the case is left
    /// untouched (no timestamp or actor update) and an empty list is returned.
    /// </summary>
    /// <exception cref="CaseRuleException">The case is closed, or the text is invalid.</exception>
    public IReadOnlyList<string> Edit(string title, string? description, string actor, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (IsClosed)
        {
            throw new CaseRuleException(CaseRuleException.CaseClosedCode, "Closed cases cannot be edited.");
        }

        var normalizedTitle = CaseTextRules.NormalizeTitle(title);
        var normalizedDescription = CaseTextRules.NormalizeDescription(description);
        ThrowIfInvalid(CaseTextRules.ValidateCase(normalizedTitle, normalizedDescription));

        var changed = DetectChanges(normalizedTitle, normalizedDescription);
        if (changed.Count == 0)
        {
            return changed;
        }

        Title = normalizedTitle;
        Description = normalizedDescription;
        Touch(actor, nowUtc);
        return changed;
    }

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
