namespace Stockly.Admin.Components;

/// <summary>
/// A range of calendar days for AD14–AD16, both ends included. The API reads them as Africa/Cairo days; the default is
/// the last 30 days ending today.
/// </summary>
public readonly record struct UsageRange(DateOnly From, DateOnly To)
{
    /// <summary>Days covered, both ends included.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>The API's rule: <c>to &gt;= from</c> and <c>to - from &lt;= 92</c>.</summary>
    public bool IsValid => To >= From && To.DayNumber - From.DayNumber <= InputLimits.MaxUsageRangeDays;

    public static UsageRange LastDays(TimeProvider clock, int days)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return new UsageRange(today.AddDays(1 - days), today);
    }
}
