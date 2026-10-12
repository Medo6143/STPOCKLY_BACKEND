using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Validation;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.UpdateProduct;

public sealed class UpdateProductCommandHandler(
    IProductRepository products,
    IProductQueries queries,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateProductCommand, Result<ProductDetail>>
{
    public async Task<Result<ProductDetail>> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var costPrice = DecimalInput.Money(request.CostPrice, FieldPath.ToCamelCase(nameof(request.CostPrice)));
        var threshold = DecimalInput.NonNegativeQuantity(request.LowStockThreshold, FieldPath.ToCamelCase(nameof(request.LowStockThreshold)));
        if (DecimalInput.FirstError(costPrice, threshold) is { } inputError)
        {
            return inputError;
        }

        var product = await products.FindAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var updated = product.Update(request.Name, request.Barcode, request.Category, costPrice.Value, threshold.Value);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (product.Barcode is { } barcode && await products.IsBarcodeUsedAsync(barcode, product.Id, cancellationToken))
        {
            return ProductErrors.DuplicateBarcode;
        }

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        var detail = await queries.FindDetailAsync(product.Id, cancellationToken);
        return detail ?? throw new InvalidOperationException("The product updated in this transaction was not found.");
    }
}
