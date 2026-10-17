namespace Stockly.Contracts.Sales;

/// <summary>
/// One sale with its lines (contract §7, S1 and S3). Money strings have 2 decimals, quantities 3. <see cref="Items"/> are
/// in request order. <see cref="Warnings"/> name the products whose stock is negative after this sale.
/// </summary>
public sealed record SaleDto(
    Guid Id,
    Guid ClientRequestId,
    string TotalAmount,
    string? Note,
    bool PriceOverridden,
    IReadOnlyList<SaleItemDto> Items,
    DateTime CreatedAt,
    Guid CreatedByUserId,
    IReadOnlyList<SaleWarningDto> Warnings);

/// <summary>
/// One sale line (contract §7). <see cref="ListUnitPrice"/> is the unit's sell price at sale time, <see cref="UnitPrice"/>
/// the price charged; <see cref="BalanceAfter"/> is the product quantity in base units right after this line.
/// </summary>
public sealed record SaleItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid ProductUnitId,
    string UnitName,
    string Quantity,
    string ListUnitPrice,
    string UnitPrice,
    bool PriceOverridden,
    string LineTotal,
    string BalanceAfter);

/// <summary>Not an error: the sale succeeded and the product's stock is negative (<see cref="SaleWarningCodes"/>).</summary>
public sealed record SaleWarningDto(string Code, Guid ProductId);

/// <summary>Codes of <see cref="SaleWarningDto"/> (contract §7, §9).</summary>
public static class SaleWarningCodes
{
    public const string NegativeBalance = "Stock.NegativeBalance";
}
