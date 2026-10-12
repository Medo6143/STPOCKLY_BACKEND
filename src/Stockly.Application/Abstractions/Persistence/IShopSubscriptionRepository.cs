using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Persistence;

public interface IShopSubscriptionRepository
{
    void Add(ShopSubscription subscription);
}
