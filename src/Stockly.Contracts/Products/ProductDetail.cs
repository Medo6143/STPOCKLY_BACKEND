namespace Stockly.Contracts.Products;

/// <summary>
/// A product with its units (contract §3). Quantities are quantity strings in the base unit (3 decimals),
/// <see cref="CostPrice"/> is a money string per base unit (2 decimals). <see cref="Units"/>: base unit first, then
/// by factor ascending. Timestamps are UTC (<see cref="DateTimeKind.Utc"/>), serialized ISO-8601 with <c>Z</c>.
/// </summary>
public sealed record ProductDetail(
    Guid Id,
    string Name,
    string? Barcode,
    string? Category,
    string Quantity,
    string CostPrice,
    string LowStockThreshold,
    bool IsLowStock,
    bool IsArchived,
    IReadOnlyList<UnitDto> Units,
    DateTime CreatedAt,
    DateTime UpdatedAt);
