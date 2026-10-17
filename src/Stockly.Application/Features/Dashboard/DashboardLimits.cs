namespace Stockly.Application.Features.Dashboard;

/// <summary>The "expiring soon" window of contract §8 (D2 <c>days</c>; D1 uses the default, ADR-0004).</summary>
public static class DashboardLimits
{
    public const int DefaultExpiringDays = 60;

    public const int MaxExpiringDays = 365;
}
