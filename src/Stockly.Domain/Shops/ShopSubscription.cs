using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

/// <summary>A shop's subscription to a <see cref="Plan"/>. A shop has at most one active subscription (database index).</summary>
public sealed class ShopSubscription : Entity, ITenantEntity
{
    /// <summary>Largest daily scan quota override the admin may set (contract AD9; <c>CHECK</c> in the database).</summary>
    public const int MaxQuotaOverride = 10_000;

    private ShopSubscription(Guid shopId, Guid planId, SubscriptionStatus status, DateOnly startsOn, DateOnly? endsOn, DateTimeOffset createdAt)
    {
        ShopId = shopId;
        PlanId = planId;
        Status = status;
        StartsOn = startsOn;
        EndsOn = endsOn;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    public Guid PlanId { get; }

    public SubscriptionStatus Status { get; private set; }

    /// <summary>Cairo business date.</summary>
    public DateOnly StartsOn { get; }

    /// <summary>Last day included; <c>null</c> is open-ended.</summary>
    public DateOnly? EndsOn { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Scans per Cairo day set by the platform admin instead of the plan's; <c>null</c> uses the plan's quota.</summary>
    public int? DailyScanQuotaOverride { get; private set; }

    /// <summary>The quota the scan applies (contract <c>effectiveDailyScanQuota</c>): the override, else the plan's.</summary>
    public int EffectiveDailyScanQuota(int planDailyScanQuota) => DailyScanQuotaOverride ?? planDailyScanQuota;

    /// <summary>An active subscription with no end date.</summary>
    public static Result<ShopSubscription> StartOpenEnded(Guid shopId, Guid planId, DateOnly startsOn, DateTimeOffset now)
    {
        if (shopId == Guid.Empty)
        {
            return ShopSubscriptionErrors.ShopRequired;
        }

        if (planId == Guid.Empty)
        {
            return ShopSubscriptionErrors.PlanRequired;
        }

        return new ShopSubscription(shopId, planId, SubscriptionStatus.Active, startsOn, endsOn: null, now);
    }

    /// <summary>
    /// An active subscription started by the platform admin (contract AD8), ending on <paramref name="endsOn"/> (inclusive)
    /// or open-ended, carrying the previous subscription's quota override.
    /// </summary>
    public static Result<ShopSubscription> Start(
        Guid shopId, Guid planId, DateOnly startsOn, DateOnly? endsOn, int? dailyScanQuotaOverride, DateTimeOffset now)
    {
        var started = StartOpenEnded(shopId, planId, startsOn, now);
        if (started.IsFailure)
        {
            return started;
        }

        if (endsOn is { } end && end < startsOn)
        {
            return ShopSubscriptionErrors.EndsBeforeStart;
        }

        var subscription = started.Value;
        var quota = subscription.SetQuotaOverride(dailyScanQuotaOverride);
        if (quota.IsFailure)
        {
            return quota.Error;
        }

        subscription.EndsOn = endsOn;
        return subscription;
    }

    /// <summary>
    /// Sets or (with <c>null</c>) removes the daily scan quota override, <c>0..</c><see cref="MaxQuotaOverride"/>. Returns
    /// whether the value changed; on failure nothing changes.
    /// </summary>
    public Result<bool> SetQuotaOverride(int? dailyScanQuota)
    {
        if (dailyScanQuota is < 0 or > MaxQuotaOverride)
        {
            return ShopSubscriptionErrors.QuotaOverrideOutOfRange;
        }

        var changed = DailyScanQuotaOverride != dailyScanQuota;
        DailyScanQuotaOverride = dailyScanQuota;
        return changed;
    }

    /// <summary>
    /// A paid month of another plan (ADR-0015 §5): an active subscription from <paramref name="today"/> ending one month
    /// later, carrying the previous subscription's quota override.
    /// </summary>
    public static Result<ShopSubscription> StartPaidMonth(
        Guid shopId, Guid planId, DateOnly today, int? dailyScanQuotaOverride, DateTimeOffset now) =>
        Start(shopId, planId, today, today.AddMonths(1), dailyScanQuotaOverride, now);

    /// <summary>
    /// A paid month of the same plan (ADR-0015 §5): <see cref="EndsOn"/> moves one month past the later of
    /// <paramref name="today"/> and the current end. An open-ended subscription stays open-ended (it is never shortened).
    /// </summary>
    public Result ExtendOneMonth(DateOnly today)
    {
        if (Status != SubscriptionStatus.Active)
        {
            return ShopSubscriptionErrors.NotActive;
        }

        if (EndsOn is { } endsOn)
        {
            EndsOn = (endsOn > today ? endsOn : today).AddMonths(1);
        }

        return Result.Success();
    }

    /// <summary>
    /// The paid period has passed (the daily downgrade job): the subscription ends and keeps its last day. Only an active
    /// subscription whose <see cref="EndsOn"/> is before <paramref name="today"/> lapses.
    /// </summary>
    public Result Lapse(DateOnly today)
    {
        if (Status != SubscriptionStatus.Active)
        {
            return ShopSubscriptionErrors.NotActive;
        }

        if (EndsOn is not { } endsOn || endsOn >= today)
        {
            return ShopSubscriptionErrors.NotLapsed;
        }

        Status = SubscriptionStatus.Ended;
        return Result.Success();
    }

    /// <summary>Ends an active subscription on <paramref name="today"/> (contract AD8: replaced by a new one from today).</summary>
    public Result End(DateOnly today)
    {
        if (Status != SubscriptionStatus.Active)
        {
            return ShopSubscriptionErrors.NotActive;
        }

        Status = SubscriptionStatus.Ended;
        EndsOn = today;
        return Result.Success();
    }
}
