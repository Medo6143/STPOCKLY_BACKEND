using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.CreateProduct;

/// <summary>
/// P3: creates a product with its base unit. A positive <see cref="OpeningQuantity"/> (Owner only) records a stock count
/// noted "Opening stock" in the same transaction. Decimal fields are the request's strings (contract §1.2).
/// </summary>
public sealed record CreateProductCommand(
    string Name,
    string BaseUnitName,
    string CostPrice,
    string BaseUnitSellPrice,
    string? Barcode,
    string? Category,
    string? LowStockThreshold,
    string? OpeningQuantity) : ICommand<ProductDetail>, IInvalidatesCatalog;
