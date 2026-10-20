using System.Text.RegularExpressions;

namespace CaseIntake.ServiceDefaults;

/// <summary>
/// Last-resort scrubber for SSN-shaped values (NNN-NN-NNNN or nine consecutive digits). Safe-field logging is the
/// primary control; this is an additional safeguard and is not a license to log submitted values.
/// The value must not touch a letter or digit on either side, so digit runs inside GUIDs, operation IDs and other hex
/// identifiers are left intact (about 1 in 17 operation IDs contained a nine-digit run and was partly redacted).
/// The tradeoff: an SSN glued to letters (for example "ssn900456789") is not caught; separators such as a space, '=',
/// ':', '-', '/' or quotes are.
/// </summary>
public static partial class SsnRedaction
{
    public const string Replacement = "[REDACTED-SSN]";

    // Same pattern as CaseIntake.TestResults.EvidenceRedactor (which may not reference this project); SsnDetectionTests
    // keeps the two in agreement.
    [GeneratedRegex(@"(?<![0-9A-Za-z])(\d{3}-\d{2}-\d{4}|\d{9})(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex SsnPattern();

    public static bool ContainsSsn(string? text) => text is not null && SsnPattern().IsMatch(text);

    public static string Redact(string text) => SsnPattern().Replace(text, Replacement);
}
