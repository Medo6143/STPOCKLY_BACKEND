using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.UpdateProduct;

/// <summary>P4: replaces the product's name, barcode, category, cost price and low-stock threshold.</summary>
public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string CostPrice,
    string LowStockThreshold,
    string? Barcode,
    string? Category) : ICommand<ProductDetail>, IInvalidatesCatalog;
