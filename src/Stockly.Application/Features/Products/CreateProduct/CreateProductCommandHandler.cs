using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Application.Validation;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Products.CreateProduct;

public sealed class CreateProductCommandHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IProductRepository products,
    IProductQueries queries,
    IStockService stock,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<CreateProductCommand, Result<ProductDetail>>
{
    public async Task<Result<ProductDetail>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId || currentTenant.ShopId is not { } shopId)
        {
            return AuthErrors.Unauthenticated;
        }

        if (request.OpeningQuantity is not null && currentUser.Role != ShopRole.Owner)
        {
            return ProductAccessErrors.OpeningStockOwnerOnly;
        }

        var costPrice = DecimalInput.Money(request.CostPrice, FieldPath.ToCamelCase(nameof(request.CostPrice)));
        var sellPrice = DecimalInput.Money(request.BaseUnitSellPrice, FieldPath.ToCamelCase(nameof(request.BaseUnitSellPrice)));
        var threshold = request.LowStockThreshold is null
            ? Result.Success(0m)
            : DecimalInput.NonNegativeQuantity(request.LowStockThreshold, FieldPath.ToCamelCase(nameof(request.LowStockThreshold)));
        var opening = request.OpeningQuantity is null
            ? Result.Success(0m)
            : DecimalInput.NonNegativeQuantity(request.OpeningQuantity, FieldPath.ToCamelCase(nameof(request.OpeningQuantity)));
        if (DecimalInput.FirstError(costPrice, sellPrice, threshold, opening) is { } inputError)
        {
            return inputError;
        }

        var product = Product.Create(
            shopId,
            request.Name,
            request.BaseUnitName,
            costPrice.Value,
            sellPrice.Value,
            threshold.Value,
            clock.UtcNow,
            request.Barcode,
            request.Category);
        if (product.IsFailure)
        {
            return product.Error;
        }

        if (product.Value.Barcode is { } barcode && await products.IsBarcodeUsedAsync(barcode, exceptProductId: null, cancellationToken))
        {
            return ProductErrors.DuplicateBarcode;
        }

        products.Add(product.Value);
        var openingStock = stock.RecordOpeningStock(product.Value, opening.Value, userId);
        if (openingStock.IsFailure)
        {
            return openingStock.Error;
        }

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        var detail = await queries.FindDetailAsync(product.Value.Id, cancellationToken);
        return detail ?? throw new InvalidOperationException("The product saved in this transaction was not found.");
    }
}
