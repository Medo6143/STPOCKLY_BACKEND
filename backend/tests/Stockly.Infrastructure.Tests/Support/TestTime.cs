using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Tests.Support;

internal static class TestTime
{
    // UTC with whole seconds: timestamptz stores microseconds, so these round-trip exactly.
    public static readonly DateTimeOffset T0 = new(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset T1 = new(2026, 10, 12, 8, 30, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset T2 = new(2026, 10, 13, 9, 45, 0, TimeSpan.Zero);
}

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
