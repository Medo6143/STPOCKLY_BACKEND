using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.UpdateProductUnit;

public sealed class UpdateProductUnitCommandHandler(IProductRepository products, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateProductUnitCommand, Result<UnitDto>>
{
    public async Task<Result<UnitDto>> Handle(UpdateProductUnitCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = request.ParseValues();
        if (values.IsFailure)
        {
            return values.Error;
        }

        var product = await products.FindAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var updated = product.UpdateUnit(request.UnitId, request.Name, values.Value.Factor, values.Value.SellPrice, request.Barcode);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var unit = product.FindUnit(request.UnitId).Value;
        if (unit.Barcode is { } barcode && await products.IsUnitBarcodeUsedAsync(barcode, unit.Id, cancellationToken))
        {
            return ProductUnitErrors.DuplicateBarcode;
        }

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? unit.ToDto() : saved.Error;
    }
}
