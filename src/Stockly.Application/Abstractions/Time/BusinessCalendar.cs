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
}
