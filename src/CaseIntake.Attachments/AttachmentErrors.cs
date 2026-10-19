using CaseIntake.Contracts.Common;

namespace CaseIntake.Attachments;

/// <summary>
/// The errors returned by the attachment service. Messages are safe to show and never echo submitted values. Codes
/// shared with the case service (Forbidden, CaseNotFound, CaseClosed) keep the same spelling so the UI maps them alike.
/// </summary>
public static class AttachmentErrors
{
    public const string FileField = "File";

    public const string ForbiddenCode = "Forbidden";
    public const string CaseNotFoundCode = "CaseNotFound";
    public const string AttachmentNotFoundCode = "AttachmentNotFound";
    public const string CaseClosedCode = "CaseClosed";
    public const string TypeNotAllowedCode = "AttachmentTypeNotAllowed";
    public const string TooLargeCode = "AttachmentTooLarge";
    public const string EmptyCode = "AttachmentEmpty";
    public const string SizeMismatchCode = "AttachmentSizeMismatch";
    public const string ContentMismatchCode = "AttachmentContentMismatch";
    public const string IncompleteCode = "UploadIncomplete";
    public const string OutcomeUnknownCode = "UploadOutcomeUnknown";

    public static Error Forbidden { get; } =
        new(ErrorKind.Forbidden, ForbiddenCode, "You do not have permission to perform this action.");

    public static Error CaseNotFound { get; } = new(ErrorKind.NotFound, CaseNotFoundCode, "The case was not found.");

    public static Error AttachmentNotFound { get; } =
        new(ErrorKind.NotFound, AttachmentNotFoundCode, "The attachment was not found.");

    public static Error CaseClosed { get; } = File(CaseClosedCode, "Files cannot be attached to a closed case.");

    public static Error Empty { get; } = File(EmptyCode, "The file is empty.");

    public static Error SizeMismatch { get; } =
        File(SizeMismatchCode, "The file's size did not match what the browser reported. Choose the file again.");

    public static Error ContentMismatch { get; } =
        File(ContentMismatchCode, "The file's content does not match its type. Choose a genuine file of an allowed type.");

    public static Error Incomplete { get; } = File(IncompleteCode, "The file could not be read completely. Try again.");

    /// <summary>The outcome of a failed commit could not be determined; the file may or may not be attached.</summary>
    public static Error OutcomeUnknown { get; } = new(ErrorKind.ExternalUnavailable, OutcomeUnknownCode,
        "The upload may not have completed. Refresh the attachment list before trying again.");

    public static Error TypeNotAllowed(AttachmentOptions options) =>
        File(TypeNotAllowedCode, $"This type of file is not allowed. {options.Describe()}");

    public static Error TooLarge(AttachmentOptions options) =>
        File(TooLargeCode, $"The file is too large. {options.Describe()}");

    private static Error File(string code, string message) =>
        new(ErrorKind.Validation, code, message, new Dictionary<string, string[]> { [FileField] = [message] });
}
