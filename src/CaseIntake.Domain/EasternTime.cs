namespace CaseIntake.Domain;

/// <summary>
/// The business calendar: times are stored in UTC and shown, and calendar days (due dates, "today", months) are counted,
/// in US Eastern time (America/New_York, EST/EDT with daylight saving).
/// </summary>
public static class EasternTime
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>The same instant with the Eastern offset of that moment.</summary>
    public static DateTimeOffset From(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>The Eastern calendar date of <paramref name="instant"/>.</summary>
    public static DateOnly Date(DateTimeOffset instant) => DateOnly.FromDateTime(From(instant).DateTime);

    /// <summary>The first instant of the Eastern calendar day <paramref name="day"/>. Midnight is never skipped by DST in this zone.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>The last instant (last tick) of the Eastern calendar day <paramref name="day"/>.</summary>
    public static DateTimeOffset EndOfDay(DateOnly day) => StartOfDay(day.AddDays(1)).AddTicks(-1);
}
