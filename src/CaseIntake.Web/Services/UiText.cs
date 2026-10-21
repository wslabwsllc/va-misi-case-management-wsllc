using System.Globalization;
using CaseIntake.Domain;
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

    public static string Priority(CasePriority priority) => priority.ToString();

    public static string PriorityCss(CasePriority priority) => "priority-" + priority.ToString().ToLowerInvariant();

    public static string Category(CaseCategory category) => category switch
    {
        CaseCategory.ContactDetails => "Contact details",
        _ => category.ToString(),
    };

    /// <summary>The owner's user name, or "Unassigned".</summary>
    public static string Owner(string? assignedTo) => string.IsNullOrEmpty(assignedTo) ? "Unassigned" : assignedTo;

    /// <summary>"10/24/2026", or "No due date".</summary>
    public static string DueDate(DateOnly? dueOn) => dueOn is { } due ? Date(due) : "No due date";

    /// <summary>A short due-date status relative to today (ET): "Overdue by 3 days", "Due today", "Due in 5 days".</summary>
    public static string? DueState(CaseStatus status, DateOnly? dueOn, DateOnly today)
    {
        if (dueOn is not { } due || status is CaseStatus.Resolved or CaseStatus.Closed)
        {
            return null;
        }
        var days = due.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => $"Overdue by {Days(-days)}",
            0 => "Due today",
            _ => $"Due in {Days(days)}",
        };
    }

    private static string Days(int days) => days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");

    /// <summary>Today's Eastern calendar date.</summary>
    public static DateOnly Today(TimeProvider time) => EasternTime.Date(time.GetUtcNow());

    /// <summary>
    /// The label of a transition button. The available targets come from the server (<c>AllowedNextStatuses</c>);
    /// only the wording is chosen here. Resolved to In Progress is the reopen edge (spec 4.3).
    /// </summary>
    public static string TransitionLabel(CaseStatus from, CaseStatus to) =>
        CaseWorkflow.IsReopen(from, to) ? $"Reopen (move to {Status(to)})" : $"Move to {Status(to)}";

    /// <summary>The culture every displayed date, time and number follows (Change 1, part 4).</summary>
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// A stored UTC instant in US Eastern time with its zone, en-US style: "10/21/2026 09:05 AM EDT" ("EST" in winter).
    /// Every timestamp the UI shows goes through here.
    /// </summary>
    public static string Time(DateTimeOffset value) => Eastern(value, "MM/dd/yyyy hh:mm tt");

    /// <summary>A compact label for charts and tight cells: "10/21 09:05 AM EDT".</summary>
    public static string ShortTime(DateTimeOffset value) => Eastern(value, "MM/dd hh:mm tt");

    /// <summary>Time of day with seconds, for "refreshed at" messages: "09:05:30 AM EDT".</summary>
    public static string ClockTime(DateTimeOffset value) => Eastern(value, "hh:mm:ss tt");

    /// <summary>A calendar date, en-US style: "10/21/2026". Dates carry no zone (due dates are Eastern calendar days).</summary>
    public static string Date(DateOnly value) => value.ToString("MM/dd/yyyy", Culture);

    private static string Eastern(DateTimeOffset value, string format) =>
        EasternTime.From(value).ToString(format, Culture) + " " + EasternTime.Abbreviation(value);

    /// <summary>For machine-readable exports (CSV), which stay in UTC.</summary>
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
