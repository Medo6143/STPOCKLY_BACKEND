using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

public static class ShopSubscriptionErrors
{
    public static readonly Error ShopRequired =
        Error.Validation("ShopSubscription.ShopRequired", "A subscription must belong to a shop.");

    public static readonly Error PlanRequired =
        Error.Validation("ShopSubscription.PlanRequired", "A subscription must reference a plan.");

    /// <summary>Internal guard (the request validator rejects it first): the end date is before the start date.</summary>
    public static readonly Error EndsBeforeStart =
        Error.Validation("ShopSubscription.EndsBeforeStart", "A subscription cannot end before it starts.");

    /// <summary>Internal guard (the request validator rejects it first): the override is outside 0..10000.</summary>
    public static readonly Error QuotaOverrideOutOfRange =
        Error.Validation("ShopSubscription.QuotaOverrideOutOfRange", "The daily scan quota override must be between 0 and 10000.");

    /// <summary>Internal guard: only an active subscription can be ended.</summary>
    public static readonly Error NotActive =
        Error.Conflict("ShopSubscription.NotActive", "The subscription is not active.");

    /// <summary>Internal guard: only a subscription whose last day has passed lapses.</summary>
    public static readonly Error NotLapsed =
        Error.Conflict("ShopSubscription.NotLapsed", "The subscription has not ended yet.");
}
