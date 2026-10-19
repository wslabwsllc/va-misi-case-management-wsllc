using System.Globalization;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Web.Services;

/// <summary>Display text shared by every page, so a status or a timestamp reads the same everywhere.</summary>
public static class UiText
{
    public const string AppName = "Case Intake and Triage";

    /// <summary>"Title - Case Intake and Triage": unique, descriptive document titles (E5-S1).</summary>
    public static string DocumentTitle(string pageTitle) => $"{pageTitle} - {AppName}";

    public static string Status(CaseStatus status) => status switch
    {
        CaseStatus.New => "New",
        CaseStatus.Triaged => "Triaged",
        CaseStatus.InProgress => "In Progress",
        CaseStatus.Resolved => "Resolved",
        CaseStatus.Closed => "Closed",
        _ => status.ToString(),
    };

    public static string StatusCss(CaseStatus status) => "status-" + status.ToString().ToLowerInvariant();

    /// <summary>
    /// The label of a transition button. The available targets come from the server (<c>AllowedNextStatuses</c>);
    /// only the wording is chosen here. Resolved to In Progress is the reopen edge (spec 4.3).
    /// </summary>
    public static string TransitionLabel(CaseStatus from, CaseStatus to) =>
        CaseWorkflow.IsReopen(from, to) ? $"Reopen (move to {Status(to)})" : $"Move to {Status(to)}";

    /// <summary>All stored times are UTC and shown with an explicit UTC label (OQ-19).</summary>
    public static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    public static string UtcDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string IsoUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string FileSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.0} MB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.0} KB"),
        1 => "1 byte",
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };

    public static string Duration(TimeSpan duration) => duration.TotalMilliseconds switch
    {
        < 1000 => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0} ms"),
        < 60_000 => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0} s"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds} s"),
    };

    public static string Percent(double fraction) =>
        string.Create(CultureInfo.InvariantCulture, $"{fraction * 100:0.#}%");

    /// <summary>Two-letter initials for the avatar, from the local part of an email or the first two words of a name.</summary>
    public static string Initials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }
        var name = displayName.Split('@')[0];
        var words = name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var letters = words.Length >= 2
            ? string.Concat(words[0][0], words[1][0])
            : name.Length >= 2 ? name[..2] : name;
        return letters.ToUpperInvariant();
    }

    /// <summary>"Showing 1–25 of 42 cases" (E5-S3).</summary>
    public static string ShowingRange(int page, int pageSize, int total, string noun)
    {
        if (total == 0)
        {
            return $"No {noun} match the current filters.";
        }
        var first = ((page - 1) * pageSize) + 1;
        var last = Math.Min(page * pageSize, total);
        return string.Create(CultureInfo.InvariantCulture, $"Showing {first}–{last} of {total} {noun}");
    }
}
