using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Products.GetProducts;

public sealed class GetProductsQueryHandler(CatalogListCache cache)
    : IRequestHandler<GetProductsQuery, Result<PagedResponse<ProductListItem>>>
{
    public async Task<Result<PagedResponse<ProductListItem>>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await cache.GetOrLoadAsync(
            request.ToFilter(lowStockOnly: false), CacheKeys.ProductList, CacheKeys.ProductListTimeToLive, cancellationToken);
    }
}
