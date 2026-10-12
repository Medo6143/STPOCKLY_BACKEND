using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Persistence;

public interface IShopRepository
{
    void Add(Shop shop);

    /// <summary>The shop's status, or <c>null</c> when it does not exist. Read without tracking on every authenticated request.</summary>
    Task<ShopStatus?> GetStatusAsync(Guid shopId, CancellationToken cancellationToken);
}
