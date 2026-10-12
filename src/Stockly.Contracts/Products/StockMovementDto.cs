namespace Stockly.Contracts.Products;

/// <summary>
/// One row of a product's stock ledger (contract §3). <see cref="QuantityChange"/> (never <c>"0.000"</c>) and
/// <see cref="BalanceAfter"/> are quantity strings in the base unit. <see cref="CreatedAt"/> is UTC, serialized with <c>Z</c>.
/// </summary>
public sealed record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string QuantityChange,
    string BalanceAfter,
    MovementReason Reason,
    Guid? InvoiceId,
    Guid? SaleId,
    string? Note,
    Guid CreatedByUserId,
    DateTime CreatedAt);
