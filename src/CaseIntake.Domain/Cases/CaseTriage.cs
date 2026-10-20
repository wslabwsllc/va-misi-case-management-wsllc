using System.Globalization;

namespace CaseIntake.Domain.Cases;

/// <summary>How urgent a case is. Numeric values are persisted and order the list (Low → Urgent); never renumber.</summary>
public enum CasePriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Urgent = 3,
}

/// <summary>What a case is about. Numeric values are persisted; never renumber, only append.</summary>
public enum CaseCategory
{
    General = 0,
    Benefits = 1,
    Claims = 2,
    Records = 3,
    Eligibility = 4,
    Billing = 5,
    Appointments = 6,
    Travel = 7,
    Facilities = 8,
    Equipment = 9,
    Pharmacy = 10,
    ContactDetails = 11,
}

/// <summary>
/// The triage fields of a case (issue #22): priority, category, owner and due date. <see cref="AssignedTo"/> is the
/// owner's user name (null = unassigned); <see cref="DueOn"/> is a UTC calendar date (null = no due date, never overdue).
/// </summary>
public sealed record CaseTriage(CasePriority Priority, CaseCategory Category, string? AssignedTo, DateOnly? DueOn)
{
    public static CaseTriage Default { get; } = new(CasePriority.Medium, CaseCategory.General, null, null);
}

/// <summary>Validation and defaults for <see cref="CaseTriage"/>, shared by the entity, the request validator and the UI.</summary>
public static class CaseTriageRules
{
    public static readonly DateOnly MinDueOn = new(2000, 1, 1);
    public static readonly DateOnly MaxDueOn = new(2100, 12, 31);

    /// <summary>Days from intake to the default due date, by priority, when the intake leaves the due date empty.</summary>
    public static int TargetDays(CasePriority priority) => priority switch
    {
        CasePriority.Urgent => 2,
        CasePriority.High => 5,
        CasePriority.Low => 30,
        _ => 10,
    };

    /// <summary>The default due date for a case created at <paramref name="createdUtc"/> with <paramref name="priority"/>.</summary>
    public static DateOnly DefaultDueOn(CasePriority priority, DateTimeOffset createdUtc) =>
        DateOnly.FromDateTime(createdUtc.UtcDateTime).AddDays(TargetDays(priority));

    /// <summary>Open (New, Triaged or In Progress) and past its due date on <paramref name="todayUtc"/>.</summary>
    public static bool IsOverdue(CaseStatus status, DateOnly? dueOn, DateOnly todayUtc) =>
        dueOn is { } due && due < todayUtc && status is CaseStatus.New or CaseStatus.Triaged or CaseStatus.InProgress;

    /// <summary>A normalized owner: trimmed, empty → null.</summary>
    public static string? NormalizeAssignee(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();

    public static IReadOnlyList<FieldError> Validate(CaseTriage triage)
    {
        ArgumentNullException.ThrowIfNull(triage);
        List<FieldError> errors = [];
        if (!Enum.IsDefined(triage.Priority))
        {
            errors.Add(new FieldError(CaseFieldNames.Priority, "Choose a valid priority."));
        }
        if (!Enum.IsDefined(triage.Category))
        {
            errors.Add(new FieldError(CaseFieldNames.Category, "Choose a valid category."));
        }
        if (triage.AssignedTo is { } owner &&
            (CaseTextRules.CountScalarValues(owner) is not { } length || length > CaseFieldLimits.ActorMaxLength))
        {
            errors.Add(new FieldError(CaseFieldNames.AssignedTo, "Choose a valid owner."));
        }
        if (triage.DueOn is { } due && (due < MinDueOn || due > MaxDueOn))
        {
            errors.Add(new FieldError(CaseFieldNames.DueOn, string.Create(CultureInfo.InvariantCulture,
                $"Due date must be between {MinDueOn:yyyy-MM-dd} and {MaxDueOn:yyyy-MM-dd}.")));
        }
        return errors;
    }
}
