namespace Stockly.Contracts.Products;

/// <summary>
/// A unit of a product (contract §3). <see cref="Factor"/> is a factor string (6 decimals): base units in one of this
/// unit, <c>"1.000000"</c> for the base unit. <see cref="SellPrice"/> is a money string (2 decimals).
/// </summary>
public sealed record UnitDto(Guid Id, string Name, string Factor, string SellPrice, string? Barcode, bool IsBase);
