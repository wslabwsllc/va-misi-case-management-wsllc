using System.Text.RegularExpressions;

namespace CaseIntake.TestResults;

/// <summary>
/// Redacts sensitive values from imported test text (failure messages, stack traces, output, browser console and
/// network logs, server logs) before it is stored or shown: SSN-shaped numbers, passwords and password-like form
/// fields, bearer tokens, cookies (including the auth and antiforgery cookies), antiforgery tokens and connection-string
/// secrets. It is applied to everything the importer stores; screenshots cannot be redacted after capture, so the E2E
/// harness masks sensitive inputs when taking them.
/// </summary>
public static partial class EvidenceRedactor
{
    public const string Mask = "[REDACTED]";

    [GeneratedRegex(@"(?<!\d)(\d{3}-\d{2}-\d{4}|\d{9})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Ssn();

    [GeneratedRegex(@"(?i)(password|passwd|pwd|secret|apikey|api_key|token|__RequestVerificationToken)(\s*[=:]\s*|""\s*:\s*"")[^\s&;,""']+", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?i)(cookie|set-cookie)(\s*[:=]\s*)[^\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex CookieHeader();

    [GeneratedRegex(@"(?i)(CaseIntake\.Auth|\.AspNetCore\.[A-Za-z.]+)=[^;\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthCookie();

    /// <summary>Additional literal secrets known to the deployment (for example the demo password).</summary>
    public static string Redact(string? text, IReadOnlyCollection<string>? knownSecrets = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }
        var result = text;
        if (knownSecrets is not null)
        {
            foreach (var secret in knownSecrets.Where(s => s.Length >= 4))
            {
                result = result.Replace(secret, Mask, StringComparison.Ordinal);
            }
        }
        result = Ssn().Replace(result, "[REDACTED-SSN]");
        result = KeyValueSecret().Replace(result, m => m.Groups[1].Value + m.Groups[2].Value + Mask);
        result = Bearer().Replace(result, "Bearer " + Mask);
        result = CookieHeader().Replace(result, m => m.Groups[1].Value + m.Groups[2].Value + Mask);
        result = AuthCookie().Replace(result, m => m.Groups[1].Value + "=" + Mask);
        return result;
    }

    /// <summary>Redacts and caps the text at <paramref name="maxLength"/> characters (without splitting a surrogate pair).</summary>
    public static string? RedactAndCap(string? text, int maxLength, IReadOnlyCollection<string>? knownSecrets = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var redacted = Redact(text, knownSecrets);
        if (redacted.Length <= maxLength)
        {
            return redacted;
        }
        const string marker = "\n[truncated]";
        var length = maxLength - marker.Length;
        if (char.IsHighSurrogate(redacted[length - 1]))
        {
            length--;
        }
        return redacted[..length] + marker;
    }
}
