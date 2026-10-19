using System.Buffers;
using System.Globalization;
using System.Text;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Domain.Attachments;

/// <summary>Attachment field limits shared by the domain, the EF mapping and the service. Lengths are Unicode scalar values.</summary>
public static class AttachmentFieldLimits
{
    public const int FileNameMaxLength = 200;
    public const int ContentTypeMaxLength = 100;

    /// <summary>Server-generated key: a GUID in "N" format (32 lowercase hex digits). Never derived from the file name.</summary>
    public const int StorageKeyLength = 32;

    /// <summary>SHA-256 of the stored bytes as lowercase hex.</summary>
    public const int Sha256HexLength = 64;
}

/// <summary>
/// Makes a user-supplied file name safe to store and to echo in a <c>Content-Disposition</c> header (E4-S5). Path
/// segments of either slash style are removed, as are control characters (including CR/LF and NUL), Unicode format
/// characters (such as the right-to-left override used to disguise extensions) and unpaired surrogates. The result is
/// trimmed and capped at 200 scalar values, keeping the extension where possible; an empty result becomes
/// <see cref="Fallback"/>. The name is only ever displayed: storage uses a server-generated key.
/// </summary>
public static class AttachmentFileName
{
    public const string Fallback = "attachment";

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return Fallback;
        }

        var lastSeparator = fileName.LastIndexOfAny(['/', '\\']);
        var leaf = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;

        var builder = new StringBuilder(leaf.Length);
        foreach (var rune in EnumerateValidRunes(leaf))
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }
            builder.Append(rune.ToString());
        }

        var cleaned = builder.ToString().Trim().TrimEnd('.').Trim();
        if (cleaned.Length == 0 || cleaned is "." or "..")
        {
            return Fallback;
        }

        return Truncate(cleaned, AttachmentFieldLimits.FileNameMaxLength);
    }

    /// <summary>The lowercase extension including the dot (<c>.pdf</c>), or an empty string.</summary>
    public static string Extension(string fileName) => Path.GetExtension(fileName).ToLowerInvariant();

    /// <summary>Caps the name at <paramref name="maxScalars"/> scalar values, preserving a short extension.</summary>
    private static string Truncate(string name, int maxScalars)
    {
        var runes = name.EnumerateRunes().ToArray();
        if (runes.Length <= maxScalars)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var extensionRunes = extension.EnumerateRunes().Count();
        var keepExtension = extensionRunes is > 0 and <= 16;
        var stemLength = maxScalars - (keepExtension ? extensionRunes : 0);
        var stem = string.Concat(runes.Take(stemLength).Select(r => r.ToString()));
        return keepExtension ? stem + extension : stem;
    }

    private static IEnumerable<Rune> EnumerateValidRunes(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed) == OperationStatus.Done)
            {
                yield return rune;
            }
            index += Math.Max(consumed, 1);
        }
    }
}

/// <summary>
/// Metadata of a file attached to a case. The bytes live in the attachment store under <see cref="StorageKey"/>;
/// every column is written once and never changed (the EF mapping rejects updates).
/// </summary>
public sealed class Attachment
{
    private Attachment()
    {
    }

    public Guid Id { get; private set; }
    public long CaseId { get; private set; }
    public Case Case { get; private set; } = null!;

    /// <summary>Sanitized display name (see <see cref="AttachmentFileName"/>).</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>The allowlisted MIME type the file is served with, never the raw value the browser claimed.</summary>
    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; private set; }

    /// <summary>Identity user ID of the uploader.</summary>
    public string UploadedById { get; private set; } = string.Empty;

    /// <summary>Uploader's display (user) name at the time of the upload.</summary>
    public string UploadedBy { get; private set; } = string.Empty;

    public static Attachment Record(Guid id, Case caseEntity, string fileName, string contentType, long sizeBytes,
        string sha256, string storageKey, DateTimeOffset uploadedAtUtc, string uploadedById, string uploadedBy)
    {
        ArgumentNullException.ThrowIfNull(caseEntity);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadedById);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadedBy);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Attachment ID must be set.", nameof(id));
        }
        if (CaseTextRules.CountScalarValues(fileName) is not { } nameLength || nameLength > AttachmentFieldLimits.FileNameMaxLength
            || contentType.Length > AttachmentFieldLimits.ContentTypeMaxLength)
        {
            throw new ArgumentException("Attachment metadata exceeds its limits.");
        }
        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Attachments cannot be empty.");
        }
        if (!IsLowerHex(sha256, AttachmentFieldLimits.Sha256HexLength))
        {
            throw new ArgumentException("SHA-256 must be 64 lowercase hex digits.", nameof(sha256));
        }
        if (!IsLowerHex(storageKey, AttachmentFieldLimits.StorageKeyLength))
        {
            throw new ArgumentException("Storage key must be 32 lowercase hex digits.", nameof(storageKey));
        }

        return new Attachment
        {
            Id = id,
            Case = caseEntity,
            CaseId = caseEntity.Id,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            StorageKey = storageKey,
            UploadedAtUtc = uploadedAtUtc.ToUniversalTime(),
            UploadedById = CaseTextRules.TruncateActor(uploadedById),
            UploadedBy = CaseTextRules.TruncateActor(uploadedBy),
        };
    }

    public static bool IsLowerHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}
