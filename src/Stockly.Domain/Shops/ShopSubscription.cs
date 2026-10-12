using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

/// <summary>A shop's subscription to a <see cref="Plan"/>. A shop has at most one active subscription (database index).</summary>
public sealed class ShopSubscription : Entity, ITenantEntity
{
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
}
