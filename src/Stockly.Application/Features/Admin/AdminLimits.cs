using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin;

/// <summary>Limits of the platform admin API (contract §11.3). The only place they are defined.</summary>
public static class AdminLimits
{
    /// <summary>Longest reason of an admin write.</summary>
    public const int ReasonMaxLength = 500;

    /// <summary>Longest AD4 search term.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>Plan codes: lower-case letters, digits and dashes, 2–32 characters (AD12).</summary>
    public const string PlanCodePattern = "^[a-z0-9-]{2,32}$";

    /// <summary>Longest plan name.</summary>
    public const int PlanNameMaxLength = 100;

    /// <summary>Largest daily scan quota of a plan or an override (AD9: <c>0..10000</c>).</summary>
    public const int MaxDailyScanQuota = ShopSubscription.MaxQuotaOverride;

    /// <summary>Largest member count of a plan.</summary>
    public const int MaxPlanMembers = 1_000;

    /// <summary>AD14–AD16: <c>to - from</c> is at most this many days.</summary>
    public const int MaxUsageRangeDays = 92;

    /// <summary>AD14–AD16 without <c>from</c>: the range covers this many days ending on <c>to</c> (default today).</summary>
    public const int DefaultUsageRangeDays = 30;

    /// <summary>Longest AD20 target type filter.</summary>
    public const int TargetTypeMaxLength = 64;

    /// <summary>AD5 <c>aiCostLast30DaysUsd</c> window.</summary>
    public static readonly TimeSpan AiCostWindow = TimeSpan.FromDays(30);

    /// <summary>AD18 <c>imageUrl</c> lifetime.</summary>
    public static readonly TimeSpan ImageUrlLifetime = TimeSpan.FromMinutes(5);
}
