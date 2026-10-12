namespace Stockly.Contracts.Products;

/// <summary>
/// P10 result. <see cref="Quantity"/> is the product quantity after the count (quantity string). When the count equals
/// the current quantity, <see cref="Changed"/> is <c>false</c> and <see cref="Movement"/> is <c>null</c>.
/// </summary>
public sealed record StockCountResponse(bool Changed, string Quantity, StockMovementDto? Movement);
