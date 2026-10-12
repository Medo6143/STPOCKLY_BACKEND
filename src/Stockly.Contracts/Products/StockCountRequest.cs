namespace Stockly.Contracts.Products;

/// <summary>P10 <c>POST /api/products/{id}/stock-counts</c>. <see cref="CountedQuantity"/> (&gt;= 0) is in the unit <see cref="UnitId"/> (absent = base unit).</summary>
public sealed record StockCountRequest(string CountedQuantity, Guid? UnitId = null);
