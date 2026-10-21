using CaseIntake.Domain;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

/// <summary>The Eastern business calendar: offsets across daylight saving, day bounds and the evening date change.</summary>
public class EasternTimeTests
{
    [TestCase("2026-01-15T17:00:00Z", "2026-01-15 12:00 -05:00")]   // EST
    [TestCase("2026-07-15T16:00:00Z", "2026-07-15 12:00 -04:00")]   // EDT
    public void Converts_to_the_offset_in_force_at_that_moment(string utc, string expected)
    {
        Assert.Equal(expected, EasternTime.From(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture))
            .ToString("yyyy-MM-dd HH:mm zzz", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public void An_evening_utc_time_is_still_the_previous_eastern_day()
    {
        var lateEvening = new DateTimeOffset(2026, 10, 21, 2, 30, 0, TimeSpan.Zero);   // 22:30 EDT on October 20

        Assert.Equal(new DateOnly(2026, 10, 20), EasternTime.Date(lateEvening));
        Assert.Equal(new DateOnly(2026, 10, 25), CaseTriageRules.DefaultDueOn(CasePriority.High, lateEvening));
    }

    [Test]
    public void Day_bounds_follow_daylight_saving()
    {
        // DST ends on November 1, 2026: that day is 25 hours long.
        var day = new DateOnly(2026, 11, 1);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), EasternTime.StartOfDay(day));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero).AddTicks(-1), EasternTime.EndOfDay(day));
    }
}
