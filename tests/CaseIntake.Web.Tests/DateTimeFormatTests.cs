using System.Globalization;
using CaseIntake.Web.Services;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Change 1, part 4: every displayed timestamp is US Eastern, en-US, with EDT or EST; dates are MM/dd/yyyy; the result
/// does not depend on the server's or thread's culture; pages do not format times themselves.
/// </summary>
public class DateTimeFormatTests
{
    [Theory]
    [InlineData("2026-07-04T16:05:00Z", "07/04/2026 12:05 PM EDT")]
    [InlineData("2026-12-25T05:00:00Z", "12/25/2026 12:00 AM EST")]
    [InlineData("2026-03-08T06:59:00Z", "03/08/2026 01:59 AM EST")]   // the minute before clocks spring forward
    [InlineData("2026-03-08T07:00:00Z", "03/08/2026 03:00 AM EDT")]   // the first minute of daylight time
    [InlineData("2026-11-01T05:30:00Z", "11/01/2026 01:30 AM EDT")]   // 1:30 happens twice when clocks fall back...
    [InlineData("2026-11-01T06:30:00Z", "11/01/2026 01:30 AM EST")]   // ...and the zone tells the two apart
    [InlineData("2026-10-21T23:30:00Z", "10/21/2026 07:30 PM EDT")]
    public void Times_are_eastern_en_US_with_the_zone_of_that_moment(string utc, string expected)
    {
        Assert.Equal(expected, UiText.Time(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Short_and_clock_times_and_dates_use_the_same_rules()
    {
        var instant = new DateTimeOffset(2026, 1, 5, 14, 7, 9, TimeSpan.Zero);

        Assert.Equal("01/05 09:07 AM EST", UiText.ShortTime(instant));
        Assert.Equal("09:07:09 AM EST", UiText.ClockTime(instant));
        Assert.Equal("01/05/2026", UiText.Date(new DateOnly(2026, 1, 5)));
        Assert.Equal("01/05/2026", UiText.DueDate(new DateOnly(2026, 1, 5)));
        Assert.Equal("No due date", UiText.DueDate(null));
    }

    [Fact]
    public void Output_does_not_depend_on_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("07/04/2026 12:05 PM EDT", UiText.Time(new DateTimeOffset(2026, 7, 4, 16, 5, 0, TimeSpan.Zero)));
            Assert.Equal("12,345", UiText.Number(12345));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Pages_do_not_format_times_themselves()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CaseIntake.Web", "Components"));
        Assert.True(Directory.Exists(root), root);
        var offenders = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f) is var text && (text.Contains("HH:mm", StringComparison.Ordinal) || text.Contains("hh:mm", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Use UiText.Time/ShortTime/ClockTime instead of a time format in: " + string.Join(", ", offenders));
    }
}
