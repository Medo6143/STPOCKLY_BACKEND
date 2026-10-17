using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.RemoveProductUnit;

/// <summary>
/// P8. A unit referenced by an invoice or sale line cannot be deleted: <c>409 ProductUnit.InUse</c> (contract §10 item 16)
/// from one tenant-filtered <c>EXISTS</c> per line table. The restrict FKs stay the last safety net: a line added by a
/// concurrent request between this check and the save is mapped to the same error by the unit of work.
/// </summary>
public sealed class RemoveProductUnitCommandHandler(
    IProductRepository products, IProductUnitUsageQueries usage, IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveProductUnitCommand, Result>
{
    public async Task<Result> Handle(RemoveProductUnitCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await products.FindAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        // Archived product, unknown unit and base unit are the Domain's answers; only a removable unit is checked for use.
        var removable = !product.IsArchived && product.Units.Any(unit => unit.Id == request.UnitId && !unit.IsBase);
        if (removable
            && (await usage.IsUsedByInvoiceLinesAsync(request.UnitId, cancellationToken)
                || await usage.IsUsedBySaleLinesAsync(request.UnitId, cancellationToken)))
        {
            return ProductUnitErrors.InUse;
        }

        var removed = product.RemoveUnit(request.UnitId);
        if (removed.IsFailure)
        {
            return removed;
        }

        return await unitOfWork.TrySaveChangesAsync(cancellationToken);
    }
}
