using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Products.GetLowStock;

public sealed class GetLowStockQueryHandler(CatalogListCache cache)
    : IRequestHandler<GetLowStockQuery, Result<PagedResponse<ProductListItem>>>
{
    public async Task<Result<PagedResponse<ProductListItem>>> Handle(GetLowStockQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await cache.GetOrLoadAsync(
            request.ToFilter(lowStockOnly: true), CacheKeys.LowStock, CacheKeys.LowStockTimeToLive, cancellationToken);
    }
}
