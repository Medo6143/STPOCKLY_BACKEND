using Stockly.Application.Abstractions.Identity;

namespace Stockly.Api.Identity;

/// <summary>
/// The per-request <see cref="ITenantScope"/>. While a scope is open, <see cref="HttpContextCurrentTenant"/> returns
/// its shop instead of the token's (registration writes a shop that has no token yet). Scopes do not nest.
/// </summary>
public sealed class RequestTenantScope : ITenantScope
{
    public Guid? ShopId { get; private set; }

    public IDisposable BeginForNewShop(Guid shopId)
    {
        if (shopId == Guid.Empty)
        {
            throw new ArgumentException("A tenant scope needs a shop.", nameof(shopId));
        }

        if (ShopId is not null)
        {
            throw new InvalidOperationException("A tenant scope is already open for this request.");
        }

        ShopId = shopId;
        return new Scope(this);
    }

    private sealed class Scope(RequestTenantScope owner) : IDisposable
    {
        public void Dispose() => owner.ShopId = null;
    }
}
