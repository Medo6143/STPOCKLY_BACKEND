namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// The tenant of a background job (docs/design/backend.md §4.1). A job has no HTTP request and no JWT, so the job sets
/// the shop it was queued for here, once, at the start of its DI scope; the Api's <c>ICurrentTenant</c> reads it. The
/// tenant query filter and the tenant stamp then work unchanged inside the job (ADR-0002). Scoped.
/// </summary>
public sealed class JobTenantContext
{
    public Guid? ShopId { get; private set; }

    public void Set(Guid shopId)
    {
        if (shopId == Guid.Empty)
        {
            throw new ArgumentException("A job tenant needs a shop.", nameof(shopId));
        }

        if (ShopId is { } current && current != shopId)
        {
            throw new InvalidOperationException("The job's tenant is already set to another shop.");
        }

        ShopId = shopId;
    }
}
