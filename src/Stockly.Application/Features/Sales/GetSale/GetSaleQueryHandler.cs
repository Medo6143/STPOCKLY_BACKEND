using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Sales;
using Stockly.Domain.Common;
using Stockly.Domain.Sales;

namespace Stockly.Application.Features.Sales.GetSale;

public sealed class GetSaleQueryHandler(ISaleQueries queries) : IRequestHandler<GetSaleQuery, Result<SaleDto>>
{
    public async Task<Result<SaleDto>> Handle(GetSaleQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sale = await queries.FindAsync(request.SaleId, cancellationToken);
        if (sale is null)
        {
            return SaleErrors.NotFound;
        }

        return sale;
    }
}
