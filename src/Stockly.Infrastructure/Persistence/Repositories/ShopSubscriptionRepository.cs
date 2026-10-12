using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Repositories;

internal sealed class ShopSubscriptionRepository(StocklyDbContext db) : IShopSubscriptionRepository
{
    public void Add(ShopSubscription subscription) => db.Set<ShopSubscription>().Add(subscription);
}
