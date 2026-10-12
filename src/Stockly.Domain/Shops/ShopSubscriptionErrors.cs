using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

public static class ShopSubscriptionErrors
{
    public static readonly Error ShopRequired =
        Error.Validation("ShopSubscription.ShopRequired", "A subscription must belong to a shop.");

    public static readonly Error PlanRequired =
        Error.Validation("ShopSubscription.PlanRequired", "A subscription must reference a plan.");
}
