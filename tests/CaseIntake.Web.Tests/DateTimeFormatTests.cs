using System.Globalization;
using CaseIntake.Web.Services;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Change 1, part 4 and the redesign: every displayed timestamp is US Eastern, MM-DD-YYYY HH:mm (24-hour) with "ET"
/// (never EST or EDT); dates are MM-DD-YYYY; the result does not depend on the server's or thread's culture; pages do not format
/// times themselves; typed dates parse as calendar days only (no zone, so no day shift).
/// </summary>
public class DateTimeFormatTests
{
    [TestCase("2026-07-04T16:05:00Z", "07-04-2026 12:05 ET")]
    [TestCase("2026-12-25T05:00:00Z", "12-25-2026 00:00 ET")]
    [TestCase("2026-03-08T06:59:00Z", "03-08-2026 01:59 ET")]   // the minute before clocks spring forward
    [TestCase("2026-03-08T07:00:00Z", "03-08-2026 03:00 ET")]   // the first minute of daylight time
    [TestCase("2026-11-01T05:30:00Z", "11-01-2026 01:30 ET")]   // 1:30 happens twice when clocks fall back; both read
                                                                  // "01:30 ET" (the redesign shows ET, not EST or EDT)
    [TestCase("2026-11-01T06:30:00Z", "11-01-2026 01:30 ET")]
    [TestCase("2026-10-21T23:30:00Z", "10-21-2026 19:30 ET")]
    public void Times_are_eastern_MM_DD_YYYY_HH_mm_with_the_zone_of_that_moment(string utc, string expected)
    {
        Assert.Equal(expected, UiText.Time(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture)));
    }

    [Test]
    public void Short_and_clock_times_and_dates_use_the_same_rules()
    {
        var instant = new DateTimeOffset(2026, 1, 5, 14, 7, 9, TimeSpan.Zero);

        Assert.Equal("01-05 09:07 ET", UiText.ShortTime(instant));
        Assert.Equal("09:07:09 ET", UiText.ClockTime(instant));
        Assert.Equal("01-05-2026", UiText.Date(new DateOnly(2026, 1, 5)));
        Assert.Equal("01-05-2026", UiText.DueDate(new DateOnly(2026, 1, 5)));
        Assert.Equal("No due date", UiText.DueDate(null));
    }

    [Test]
    public void Output_does_not_depend_on_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("07-04-2026 12:05 ET", UiText.Time(new DateTimeOffset(2026, 7, 4, 16, 5, 0, TimeSpan.Zero)));
            Assert.Equal("07-04-2026", UiText.Date(new DateOnly(2026, 7, 4)));
            Assert.Equal("12,345", UiText.Number(12345));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestCase("10-21-2026", 2026, 10, 21)]
    [TestCase("1-5-2026", 2026, 1, 5)]
    [TestCase("01/05/2026", 2026, 1, 5)]
    [TestCase(" 12-31-2026 ", 2026, 12, 31)]
    [TestCase("2026-03-08", 2026, 3, 8)]     // ISO from older links and the URL
    [TestCase("11-01-2026", 2026, 11, 1)]    // the day clocks fall back: still the same calendar day
    public void Typed_dates_parse_as_calendar_days(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), UiText.ParseDate(text));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("02-30-2026")]
    [TestCase("13-01-2026")]
    [TestCase("21-10-2026")]
    [TestCase("10-21-26")]
    [TestCase("tomorrow")]
    public void Anything_else_is_not_a_date(string? text)
    {
        Assert.Null(UiText.ParseDate(text));
    }

    [Test]
    public void Pages_do_not_format_times_themselves()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CaseIntake.Web", "Components"));
        Assert.True(Directory.Exists(root), root);
        var offenders = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f) is var text && (text.Contains("HH:mm", StringComparison.Ordinal) || text.Contains("hh:mm", StringComparison.Ordinal)
                || text.Contains("MM/dd", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Use UiText.Time/ShortTime/ClockTime/Date instead of a date or time format in: " + string.Join(", ", offenders));
    }
}
