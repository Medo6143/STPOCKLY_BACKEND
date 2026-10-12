namespace Stockly.Contracts.Products;

/// <summary>
/// P3 <c>POST /api/products</c>. Money and quantities are decimal strings (contract §1.2). <see cref="OpeningQuantity"/>
/// (base unit, Owner only) records an opening stock count when greater than zero. Optional fields have defaults so the
/// spec does not list them as required.
/// </summary>
public sealed record CreateProductRequest(
    string Name,
    string BaseUnitName,
    string CostPrice,
    string BaseUnitSellPrice,
    string? Barcode = null,
    string? Category = null,
    string? LowStockThreshold = null,
    string? OpeningQuantity = null);
