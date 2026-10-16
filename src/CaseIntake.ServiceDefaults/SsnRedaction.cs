using System.Text.RegularExpressions;

namespace CaseIntake.ServiceDefaults;

/// <summary>
/// Last-resort scrubber for SSN-shaped values (NNN-NN-NNNN or nine consecutive digits). Safe-field logging is the
/// primary control; this is an additional safeguard and is not a license to log submitted values.
/// </summary>
public static partial class SsnRedaction
{
    public const string Replacement = "[REDACTED-SSN]";

    [GeneratedRegex(@"(?<!\d)(\d{3}-\d{2}-\d{4}|\d{9})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SsnPattern();

    public static bool ContainsSsn(string? text) => text is not null && SsnPattern().IsMatch(text);

    public static string Redact(string text) => SsnPattern().Replace(text, Replacement);
}
