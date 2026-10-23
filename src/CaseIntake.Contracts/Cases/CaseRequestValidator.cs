using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Cases;

/// <summary>
/// Maps the domain <see cref="CaseTextRules"/> to request errors. The case service always calls it, and Blazor forms
/// call it too and copy <see cref="Error.FieldErrors"/> into their <c>ValidationMessageStore</c>, so client and
/// server apply exactly the same rules (E2-S2).
/// </summary>
public static class CaseRequestValidator
{
    public const string ValidationCode = "Validation";
    public const string ValidationMessage = "Some fields need attention.";
    public const string VeteranRequiredMessage = "Look up and attach a veteran before submitting.";

    /// <returns>A <see cref="ErrorKind.Validation"/> error with field errors, or null when the request is valid.</returns>
    public static Error? Validate(CreateCaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<FieldError> errors = [.. CaseTextRules.ValidateCase(CaseTextRules.NormalizeTitle(request.Title), request.Description)];
        if (request.Veteran is { } veteran)
        {
            errors.AddRange(CaseTextRules.ValidateVeteran(veteran.Name, veteran.Address, veteran.SsnLast4));
            errors.AddRange(CaseTextRules.ValidateServiceHistory(veteran.HistoryStatus, veteran.History));
        }
        else
        {
            errors.Add(new FieldError(CaseFieldNames.Veteran, VeteranRequiredMessage));
        }
        if (request.Triage is { } triage)
        {
            errors.AddRange(CaseTriageRules.Validate(triage));
        }

        return ToError(errors);
    }

    /// <returns>A <see cref="ErrorKind.Validation"/> error with field errors, or null when the request is valid.</returns>
    public static Error? Validate(UpdateCaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<FieldError> errors = [.. CaseTextRules.ValidateCase(CaseTextRules.NormalizeTitle(request.Title), request.Description)];
        if (request.Triage is { } triage)
        {
            errors.AddRange(CaseTriageRules.Validate(triage));
        }
        return ToError(errors);
    }

    public static Error? ToError(IEnumerable<FieldError> errors)
    {
        var fieldErrors = errors
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return fieldErrors.Count == 0
            ? null
            : new Error(ErrorKind.Validation, ValidationCode, ValidationMessage, fieldErrors);
    }
}
