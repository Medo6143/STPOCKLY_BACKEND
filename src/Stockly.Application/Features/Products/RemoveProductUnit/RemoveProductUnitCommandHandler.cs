using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.RemoveProductUnit;

/// <summary>
/// <c>ProductUnit.InUse</c> (409) needs invoice and sale lines, which do not exist before Sprint 2; until then a unit
/// can always be removed unless the product is archived or the unit is the base unit.
/// </summary>
public sealed class RemoveProductUnitCommandHandler(IProductRepository products, IUnitOfWork unitOfWork)
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

        var removed = product.RemoveUnit(request.UnitId);
        if (removed.IsFailure)
        {
            return removed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
