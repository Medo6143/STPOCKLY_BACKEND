namespace Stockly.Contracts.Dashboard;

/// <summary>
/// D2 row (contract §8, ADR-0004: a hint, not an exact count): a confirmed invoice line with an expiry date whose product
/// still has stock. <see cref="LineQuantity"/> is the line's quantity in the product's base unit; <see cref="CurrentQuantity"/>
/// the product's quantity now (both quantity strings, 3 decimals).
/// </summary>
public sealed record ExpiringItem(
    Guid ProductId,
    string ProductName,
    DateOnly ExpiryDate,
    string? BatchNo,
    Guid InvoiceId,
    string LineQuantity,
    string CurrentQuantity);
