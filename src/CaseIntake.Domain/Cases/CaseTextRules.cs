using System.Buffers;
using System.Globalization;
using System.Text;

namespace CaseIntake.Domain.Cases;

/// <summary>Field limits shared by the domain, the EF mapping, the request validator and the UI. Lengths are Unicode scalar values.</summary>
public static class CaseFieldLimits
{
    public const int TitleMinLength = 5;
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int VeteranNameMaxLength = 200;
    public const int VeteranAddressMaxLength = 500;
    public const int SsnLast4Length = 4;
    public const int ActorMaxLength = 256;
    public const int AuditActionMaxLength = 50;
    public const int AuditDetailsMaxLength = 1000;
    public const int SeedKeyMaxLength = 64;
    public const int SearchMaxLength = 200;
}

/// <summary>Field names used in validation results, so UI forms can map errors to their inputs.</summary>
public static class CaseFieldNames
{
    public const string Title = "Title";
    public const string Description = "Description";
    public const string Veteran = "Veteran";
    public const string VeteranName = "Veteran.Name";
    public const string VeteranAddress = "Veteran.Address";
    public const string VeteranSsnLast4 = "Veteran.SsnLast4";
    public const string Priority = "Priority";
    public const string Category = "Category";
    public const string AssignedTo = "AssignedTo";
    public const string DueOn = "DueOn";
}

/// <summary>One violated rule for one field.</summary>
public readonly record struct FieldError(string Field, string Message);

/// <summary>
/// Pure normalization and validation for case text (E2-S2, OQ-12). Intake, edit, the domain entity and the UI all call
/// these rules, so they cannot disagree. Title is trimmed and then counted; Description is never trimmed or
/// truncated. Lengths count Unicode scalar values (what PostgreSQL <c>char_length</c> counts), not UTF-16 units.
/// Malformed UTF-16 (unpaired surrogates) and NUL are rejected because PostgreSQL cannot store them.
/// </summary>
public static class CaseTextRules
{
    public const string TitleRequiredMessage = "Title is required.";
    public static readonly string TitleLengthMessage = string.Create(CultureInfo.InvariantCulture,
        $"Title must be between {CaseFieldLimits.TitleMinLength} and {CaseFieldLimits.TitleMaxLength} characters.");
    public static readonly string DescriptionLengthMessage = string.Create(CultureInfo.InvariantCulture,
        $"Description must be {CaseFieldLimits.DescriptionMaxLength:N0} characters or fewer.");

    public static string NormalizeTitle(string? title) => title?.Trim() ?? string.Empty;

    /// <summary>Empty descriptions are stored as null; otherwise the text is kept exactly as entered.</summary>
    public static string? NormalizeDescription(string? description) =>
        string.IsNullOrEmpty(description) ? null : description;

    /// <summary>Number of Unicode scalar values, or null when the text contains an unpaired surrogate or NUL.</summary>
    public static int? CountScalarValues(string text)
    {
        var count = 0;
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done || rune.Value == 0)
            {
                return null;
            }

            count++;
            remaining = remaining[consumed..];
        }

        return count;
    }

    /// <summary>Validates an already-normalized title.</summary>
    public static IEnumerable<FieldError> ValidateTitle(string normalizedTitle)
    {
        if (normalizedTitle.Length == 0)
        {
            yield return new FieldError(CaseFieldNames.Title, TitleRequiredMessage);
            yield break;
        }

        var length = CountScalarValues(normalizedTitle);
        if (length is null)
        {
            yield return InvalidCharacters(CaseFieldNames.Title, "Title");
        }
        else if (length is < CaseFieldLimits.TitleMinLength or > CaseFieldLimits.TitleMaxLength)
        {
            yield return new FieldError(CaseFieldNames.Title, TitleLengthMessage);
        }
    }

    public static IEnumerable<FieldError> ValidateDescription(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            yield break;
        }

        var length = CountScalarValues(description);
        if (length is null)
        {
            yield return InvalidCharacters(CaseFieldNames.Description, "Description");
        }
        else if (length > CaseFieldLimits.DescriptionMaxLength)
        {
            yield return new FieldError(CaseFieldNames.Description, DescriptionLengthMessage);
        }
    }

    /// <summary>Title (already normalized) and description together, as used by intake and edit.</summary>
    public static IReadOnlyList<FieldError> ValidateCase(string normalizedTitle, string? description) =>
        [.. ValidateTitle(normalizedTitle), .. ValidateDescription(description)];

    /// <summary>The optional veteran snapshot captured from the lookup at intake (A8, OQ-5b).</summary>
    public static IReadOnlyList<FieldError> ValidateVeteran(string? name, string? address, string? ssnLast4)
    {
        List<FieldError> errors = [];
        RequiredText(name, CaseFieldNames.VeteranName, "Veteran name", CaseFieldLimits.VeteranNameMaxLength, errors);
        RequiredText(address, CaseFieldNames.VeteranAddress, "Veteran address", CaseFieldLimits.VeteranAddressMaxLength, errors);
        if (!IsSsnLast4(ssnLast4))
        {
            errors.Add(new FieldError(CaseFieldNames.VeteranSsnLast4, "SSN last four must be exactly four digits (0-9)."));
        }

        return errors;
    }

    /// <summary>
    /// Caps an actor identifier or display name at <see cref="CaseFieldLimits.ActorMaxLength"/> UTF-16 units (the
    /// column limit) without splitting a surrogate pair, which PostgreSQL could not store.
    /// </summary>
    public static string TruncateActor(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length <= CaseFieldLimits.ActorMaxLength)
        {
            return value;
        }
        var length = char.IsHighSurrogate(value[CaseFieldLimits.ActorMaxLength - 1])
            ? CaseFieldLimits.ActorMaxLength - 1
            : CaseFieldLimits.ActorMaxLength;
        return value[..length];
    }

    public static bool IsSsnLast4(string? value) =>
        value is { Length: CaseFieldLimits.SsnLast4Length } && value.All(char.IsAsciiDigit);

    private static void RequiredText(string? value, string field, string label, int maxLength, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new FieldError(field, $"{label} is required."));
            return;
        }

        var length = CountScalarValues(value);
        if (length is null)
        {
            errors.Add(InvalidCharacters(field, label));
        }
        else if (length > maxLength)
        {
            errors.Add(new FieldError(field, string.Create(CultureInfo.InvariantCulture, $"{label} must be {maxLength:N0} characters or fewer.")));
        }
    }

    private static FieldError InvalidCharacters(string field, string label) =>
        new(field, $"{label} contains characters that are not allowed.");
}
