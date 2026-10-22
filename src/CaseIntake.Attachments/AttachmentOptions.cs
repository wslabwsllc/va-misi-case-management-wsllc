using System.Globalization;
using CaseIntake.Domain.Attachments;
using Microsoft.Extensions.Options;

namespace CaseIntake.Attachments;

/// <summary>One allowed attachment type: a MIME type and the file extensions that may carry it (decision D6).</summary>
public sealed class AllowedAttachmentType
{
    public string ContentType { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = [];
}

/// <summary>
/// Bound from the <c>Attachments</c> configuration section (A9, OQ-6). When <see cref="AllowedTypes"/> is not
/// configured, the adopted defaults apply: PDF, PNG, JPEG, plain text and DOCX. Validated at startup.
/// </summary>
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";
    public const long DefaultMaxBytes = 10 * 1024 * 1024;   // 10 MB (10,485,760 bytes)

    /// <summary>
    /// Directory holding <c>tmp/</c> (uploads in progress) and <c>blobs/</c> (committed content), outside wwwroot.
    /// Compose sets <c>/attachments</c> (the named volume). When unset, <see cref="DefaultRootPath"/> is used, one
    /// fixed folder per machine, so a locally run web app and migrator always agree regardless of working directory.
    /// A relative value is resolved against the process working directory; prefer an absolute path.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    public static string DefaultRootPath { get; } = Path.Combine(Path.GetTempPath(), "caseintake-attachments");

    public long MaxBytes { get; set; } = DefaultMaxBytes;

    public List<AllowedAttachmentType> AllowedTypes { get; set; } = [];

    /// <summary>Age after which the startup reconciler may delete leftover temporary or unreferenced files.</summary>
    public TimeSpan OrphanGracePeriod { get; set; } = TimeSpan.FromHours(1);

    public static IReadOnlyList<AllowedAttachmentType> DefaultAllowedTypes { get; } =
    [
        new() { ContentType = "application/pdf", Extensions = [".pdf"] },
        new() { ContentType = "image/png", Extensions = [".png"] },
        new() { ContentType = "image/jpeg", Extensions = [".jpg", ".jpeg"] },
        new() { ContentType = "text/plain", Extensions = [".txt"] },
        new() { ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Extensions = [".docx"] },
    ];

    /// <summary>The allowlisted MIME type for this content type and extension pair, or null when the pair is not allowed.</summary>
    public string? MatchAllowedType(string? contentType, string extension)
    {
        var mediaType = NormalizeMediaType(contentType);
        return AllowedTypes.FirstOrDefault(t =>
                string.Equals(t.ContentType, mediaType, StringComparison.OrdinalIgnoreCase)
                && t.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            ?.ContentType.ToLowerInvariant();
    }

    /// <summary>Human-readable allowlist and size limit, for validation messages and the UI hint.</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"Allowed types: {string.Join(", ", AllowedTypes.SelectMany(t => t.Extensions))}. Maximum size: {FormatSize(MaxBytes)}.");

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.##} MB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.##} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };

    /// <summary>"Text/Plain; charset=utf-8" → "text/plain".</summary>
    internal static string NormalizeMediaType(string? contentType) =>
        (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
}

/// <summary>Fills the defaults and rejects inconsistent configuration before the app accepts any upload.</summary>
internal sealed class AttachmentOptionsSetup : IPostConfigureOptions<AttachmentOptions>, IValidateOptions<AttachmentOptions>
{
    public void PostConfigure(string? name, AttachmentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            options.RootPath = AttachmentOptions.DefaultRootPath;
        }

        // Applied here rather than as property initializers: configuration binding appends to a non-empty list.
        var source = options.AllowedTypes.Count == 0 ? AttachmentOptions.DefaultAllowedTypes : options.AllowedTypes;
        // Normalized once, so matching and validation compare the same values (" Application/PDF " → "application/pdf").
        options.AllowedTypes = [.. source.Select(t => new AllowedAttachmentType
        {
            ContentType = (t.ContentType ?? string.Empty).Trim().ToLowerInvariant(),
            Extensions = [.. t.Extensions.Select(e => (e ?? string.Empty).Trim().ToLowerInvariant())],
        })];
    }

    public ValidateOptionsResult Validate(string? name, AttachmentOptions options)
    {
        List<string> failures = [];
        if (options.MaxBytes <= 0)
        {
            failures.Add("Attachments:MaxBytes must be positive.");
        }
        if (options.OrphanGracePeriod < TimeSpan.Zero)
        {
            failures.Add("Attachments:OrphanGracePeriod cannot be negative.");
        }

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in options.AllowedTypes)
        {
            var mediaType = type.ContentType;
            if (mediaType.Length is 0 or > AttachmentFieldLimits.ContentTypeMaxLength || !mediaType.Contains('/')
                || mediaType.Any(c => char.IsWhiteSpace(c) || c == ';'))
            {
                failures.Add($"Attachments:AllowedTypes has an invalid content type '{type.ContentType}'.");
            }
            if (type.Extensions.Count == 0)
            {
                failures.Add($"Attachments:AllowedTypes '{type.ContentType}' lists no extensions.");
            }
            foreach (var extension in type.Extensions)
            {
                if (extension.Length < 2 || extension[0] != '.' || extension[1..].Any(c => !char.IsAsciiLetterOrDigit(c)))
                {
                    failures.Add($"Attachments:AllowedTypes extension '{extension}' must look like '.pdf'.");
                }
                else if (owners.TryGetValue(extension, out var owner) && !string.Equals(owner, mediaType, StringComparison.Ordinal))
                {
                    failures.Add($"Attachments:AllowedTypes maps '{extension}' to both '{owner}' and '{mediaType}'.");
                }
                owners.TryAdd(extension, mediaType);
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
