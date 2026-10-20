using System.Globalization;

namespace CaseIntake.Web.Components.Pages.Cases;

/// <summary>Case list URLs for drill-through from the dashboard (issue #21), in the list page's query-string format.</summary>
public static class CaseListLinks
{
    private static string Day(DateTimeOffset value) => Domain.EasternTime.Date(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Cases created on the Eastern days of two instants (whole days), newest first.</summary>
    public static string CreatedIn(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        $"cases?createdFrom={Day(fromUtc)}&createdTo={Day(toUtc)}&sort=CreatedAt&dir=desc";

    /// <summary>Resolved or Closed cases last updated on the Eastern days of two instants (the resolution trend's rule).</summary>
    public static string CompletedIn(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        $"cases?status=Resolved&status=Closed&updatedFrom={Day(fromUtc)}&updatedTo={Day(toUtc)}";

    /// <summary>New cases created on or after the Eastern day of <paramref name="sinceUtc"/>, newest first.</summary>
    public static string NewSince(DateTimeOffset sinceUtc) =>
        $"cases?status=New&createdFrom={Day(sinceUtc)}&sort=CreatedAt&dir=desc";
}
