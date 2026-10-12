using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.GetProductMovements;

public sealed class GetProductMovementsQueryHandler(IProductQueries queries)
    : IRequestHandler<GetProductMovementsQuery, Result<PagedResponse<StockMovementDto>>>
{
    public async Task<Result<PagedResponse<StockMovementDto>>> Handle(GetProductMovementsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await queries.ExistsAsync(request.ProductId, cancellationToken))
        {
            return ProductErrors.NotFound;
        }

        return await queries.ListMovementsAsync(request.ProductId, request.Page, request.PageSize, cancellationToken);
    }
}
