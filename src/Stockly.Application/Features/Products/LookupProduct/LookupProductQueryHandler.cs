using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.LookupProduct;

/// <summary>P12 (contract §5, §10 item 12). Barcodes are stored trimmed, so the input is trimmed too.</summary>
public sealed class LookupProductQueryHandler(IProductQueries queries) : IRequestHandler<LookupProductQuery, Result<ProductLookupResponse>>
{
    public async Task<Result<ProductLookupResponse>> Handle(LookupProductQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Barcode);

        var found = await queries.FindByBarcodeAsync(request.Barcode.Trim(), cancellationToken);
        if (found is null)
        {
            return ProductErrors.NotFound;
        }

        return found;
    }
}
