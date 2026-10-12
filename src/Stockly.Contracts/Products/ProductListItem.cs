namespace Stockly.Contracts.Products;

/// <summary>
/// A row of the product list (contract §3). <see cref="Quantity"/> and <see cref="LowStockThreshold"/> are quantity
/// strings in the base unit (3 decimals); the quantity may be negative. <see cref="IsLowStock"/> = quantity &lt;= threshold.
/// </summary>
public sealed record ProductListItem(
    Guid Id,
    string Name,
    string Quantity,
    BaseUnitSummary BaseUnit,
    string LowStockThreshold,
    bool IsLowStock,
    bool IsArchived);

/// <summary>The base unit of a list row. <see cref="SellPrice"/> is a money string (2 decimals).</summary>
public sealed record BaseUnitSummary(Guid Id, string Name, string SellPrice);
