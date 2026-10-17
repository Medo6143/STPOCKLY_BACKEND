namespace Stockly.Application.Abstractions.Time;

/// <summary>Business dates are Cairo dates (shops are in Egypt): subscriptions, daily quotas.</summary>
public static class BusinessCalendar
{
    public const string TimeZoneId = "Africa/Cairo";

    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public static DateOnly Today(this IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Cairo).DateTime);
    }

    /// <summary>The UTC instant today's Cairo day started (the daily scan quota fallback counts from here).</summary>
    public static DateTimeOffset CairoDayStartUtc(this IClock clock) => StartOfDayUtc(clock.Today());

    /// <summary>The UTC instants today's Cairo day starts (inclusive) and ends (exclusive): "today" on the dashboard (D1).</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) CairoDayRangeUtc(this IClock clock)
    {
        var today = clock.Today();
        return (StartOfDayUtc(today), StartOfDayUtc(today.AddDays(1)));
    }

    /// <summary>Time until the next Cairo midnight, when the daily scan quota resets (contract I1 <c>Retry-After</c>).</summary>
    public static TimeSpan UntilNextCairoMidnight(this IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return StartOfDayUtc(clock.Today().AddDays(1)) - clock.UtcNow;
    }

    // Egypt starts summer time at local midnight, so 00:00 can be skipped: the day then starts at the first valid hour.
    private static DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (Cairo.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Cairo), TimeSpan.Zero);
    }
}
