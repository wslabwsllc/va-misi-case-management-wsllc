namespace CaseIntake.Attachments;

/// <summary>
/// Leading-byte ("magic number") checks for the allowlisted binary types, so a file renamed to <c>.pdf</c> with a PDF
/// content type must also start like a PDF. Defence in depth, not proof of safety: the content is never parsed and
/// malware scanning stays out of scope (4.7). Types without a known signature (plain text, configured additions)
/// are accepted on their type and extension alone.
/// </summary>
internal static class FileSignatures
{
    /// <summary>Bytes captured from the start of each upload; enough for every signature below.</summary>
    public const int HeaderLength = 8;

    private static readonly Dictionary<string, byte[][]> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ["%PDF-"u8.ToArray()],
        ["image/png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]],
        // DOCX is a ZIP package.
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [[0x50, 0x4B, 0x03, 0x04]],
    };

    public static bool Matches(string contentType, ReadOnlySpan<byte> header)
    {
        if (!Known.TryGetValue(contentType, out var signatures))
        {
            return true;
        }
        foreach (var signature in signatures)
        {
            if (header.StartsWith(signature))
            {
                return true;
            }
        }
        return false;
    }
}
