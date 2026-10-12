using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Repositories;

internal sealed class ShopRepository(StocklyDbContext db) : IShopRepository
{
    public void Add(Shop shop) => db.Shops.Add(shop);

    public Task<ShopStatus?> GetStatusAsync(Guid shopId, CancellationToken cancellationToken) =>
        db.Shops.AsNoTracking()
            .Where(shop => shop.Id == shopId)
            .Select(shop => (ShopStatus?)shop.Status)
            .SingleOrDefaultAsync(cancellationToken);
}
