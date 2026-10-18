namespace Stockly.Admin.Tests.Support;

/// <summary>A clock stopped at <see cref="Now"/>, in UTC, so date ranges in tests are fixed.</summary>
public sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
