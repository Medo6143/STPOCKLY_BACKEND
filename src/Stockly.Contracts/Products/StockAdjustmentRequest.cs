namespace Stockly.Contracts.Products;

/// <summary>
/// P9 <c>POST /api/products/{id}/adjustments</c>. <see cref="QuantityChange"/> is a signed quantity string in the unit
/// <see cref="UnitId"/> (absent = base unit). <see cref="Note"/> is required.
/// </summary>
public sealed record StockAdjustmentRequest(string QuantityChange, string? Note = null, Guid? UnitId = null);
