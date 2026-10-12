namespace Stockly.Contracts.Products;

/// <summary>
/// P4 <c>PUT /api/products/{id}</c>. Replaces the product's details; the base unit's name and price change through P7.
/// <see cref="Barcode"/> and <see cref="Category"/> are optional: absent or blank clears them.
/// </summary>
public sealed record UpdateProductRequest(
    string Name,
    string CostPrice,
    string LowStockThreshold,
    string? Barcode = null,
    string? Category = null);
