using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.AddProductUnit;

public sealed class AddProductUnitCommandHandler(IProductRepository products, IUnitOfWork unitOfWork)
    : IRequestHandler<AddProductUnitCommand, Result<UnitDto>>
{
    public async Task<Result<UnitDto>> Handle(AddProductUnitCommand request, CancellationToken cancellationToken)
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

        var unit = product.AddUnit(request.Name, values.Value.Factor, values.Value.SellPrice, request.Barcode);
        if (unit.IsFailure)
        {
            return unit.Error;
        }

        if (unit.Value.Barcode is { } barcode && await products.IsUnitBarcodeUsedAsync(barcode, unit.Value.Id, cancellationToken))
        {
            return ProductUnitErrors.DuplicateBarcode;
        }

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? unit.Value.ToDto() : saved.Error;
    }
}
