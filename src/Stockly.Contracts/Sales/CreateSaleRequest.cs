namespace Stockly.Contracts.Sales;

/// <summary>
/// S1 <c>POST /api/sales</c>. <see cref="ClientRequestId"/> is created once per sale on the device and reused on every
/// retry (ADR-0006). 1..100 items.
/// </summary>
public sealed record CreateSaleRequest(Guid ClientRequestId, IReadOnlyList<SaleItemRequest> Items, string? Note = null);

/// <summary>
/// One sale line: a product and one of its units. <see cref="Quantity"/> &gt; 0 in that unit (decimal string, 3 decimals);
/// <see cref="UnitPrice"/> absent charges the unit's sell price (money string).
/// </summary>
public sealed record SaleItemRequest(Guid ProductId, Guid ProductUnitId, string Quantity, string? UnitPrice = null);
