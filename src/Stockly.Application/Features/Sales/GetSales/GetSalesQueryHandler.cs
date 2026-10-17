using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Common;
using Stockly.Contracts.Sales;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Sales.GetSales;

/// <summary>S2. The range bounds are compared in UTC (timestamptz).</summary>
public sealed class GetSalesQueryHandler(ISaleQueries queries) : IRequestHandler<GetSalesQuery, Result<PagedResponse<SaleListItem>>>
{
    public async Task<Result<PagedResponse<SaleListItem>>> Handle(GetSalesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await queries.ListAsync(
            request.From?.ToUniversalTime(), request.To?.ToUniversalTime(), request.Page, request.PageSize, cancellationToken);
    }
}
