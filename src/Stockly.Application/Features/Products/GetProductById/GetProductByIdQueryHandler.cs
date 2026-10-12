using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.GetProductById;

public sealed class GetProductByIdQueryHandler(IProductQueries queries) : IRequestHandler<GetProductByIdQuery, Result<ProductDetail>>
{
    public async Task<Result<ProductDetail>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var detail = await queries.FindDetailAsync(request.ProductId, cancellationToken);
        return detail is null ? ProductErrors.NotFound : detail;
    }
}
