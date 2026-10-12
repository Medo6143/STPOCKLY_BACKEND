using System.Globalization;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using ContractMovementReason = Stockly.Contracts.Products.MovementReason;
using DomainMovementReason = Stockly.Domain.Stock.MovementReason;

namespace Stockly.Application.Features.Products;

/// <summary>
/// Decimal strings with fixed decimals (contract §1.2: money 2, quantity 3, factor 6) and Domain → contract DTOs.
/// Contract enums share the Domain enums' names and values (guarded by a unit test).
/// </summary>
public static class ProductMapping
{
    private static readonly string MoneyFormat = "F" + Precision.MoneyDecimals.ToString(CultureInfo.InvariantCulture);
    private static readonly string QuantityFormat = "F" + Precision.QuantityDecimals.ToString(CultureInfo.InvariantCulture);
    private static readonly string FactorFormat = "F" + Precision.FactorDecimals.ToString(CultureInfo.InvariantCulture);

    public static string Money(decimal value) => value.ToString(MoneyFormat, CultureInfo.InvariantCulture);

    public static string Quantity(decimal value) => value.ToString(QuantityFormat, CultureInfo.InvariantCulture);

    public static string Factor(decimal value) => value.ToString(FactorFormat, CultureInfo.InvariantCulture);

    public static UnitDto ToDto(this ProductUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return new UnitDto(unit.Id, unit.Name, Factor(unit.Factor), Money(unit.SellPrice), unit.Barcode, unit.IsBase);
    }

    public static StockMovementDto ToDto(this StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        return new StockMovementDto(
            movement.Id,
            movement.ProductId,
            Quantity(movement.QuantityChange),
            Quantity(movement.BalanceAfter),
            ToContract(movement.Reason),
            movement.InvoiceId,
            movement.SaleId,
            movement.Note,
            movement.CreatedByUserId,
            movement.CreatedAt.UtcDateTime);
    }

    public static ContractMovementReason ToContract(DomainMovementReason reason) => (ContractMovementReason)reason;
}
