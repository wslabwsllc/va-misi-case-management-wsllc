using CaseIntake.Domain.Cases;

namespace CaseIntake.Web.Components.Pages.Cases;

/// <summary>
/// The triage inputs of a case form (issue #22). Intake and edit form models implement it so the triage fields share
/// the form's <c>EditContext</c>: server field errors (keyed by <see cref="CaseFieldNames"/>, which equal these property
/// names) appear next to the inputs and in the form's error summary.
/// </summary>
public interface ITriageForm
{
    CasePriority Priority { get; set; }
    CaseCategory Category { get; set; }

    /// <summary>Owner user name; empty = unassigned.</summary>
    string? AssignedTo { get; set; }
    DateOnly? DueOn { get; set; }
}

public static class TriageFormExtensions
{
    public static CaseTriage ToTriage(this ITriageForm form) =>
        new(form.Priority, form.Category, string.IsNullOrWhiteSpace(form.AssignedTo) ? null : form.AssignedTo, form.DueOn);

    public static void Load(this ITriageForm form, CaseTriage triage)
    {
        form.Priority = triage.Priority;
        form.Category = triage.Category;
        form.AssignedTo = triage.AssignedTo;
        form.DueOn = triage.DueOn;
    }

    /// <summary>Error summary entries (field name → input ID) for the triage inputs with this ID prefix.</summary>
    public static IEnumerable<KeyValuePair<string, string>> SummaryFields(string idPrefix) =>
    [
        new(CaseFieldNames.Priority, $"{idPrefix}-priority"),
        new(CaseFieldNames.Category, $"{idPrefix}-category"),
        new(CaseFieldNames.AssignedTo, $"{idPrefix}-owner"),
        new(CaseFieldNames.DueOn, $"{idPrefix}-due"),
    ];

    public static bool IsTriageField(string field) =>
        field is CaseFieldNames.Priority or CaseFieldNames.Category or CaseFieldNames.AssignedTo or CaseFieldNames.DueOn;
}
