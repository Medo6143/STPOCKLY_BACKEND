using Stockly.Domain.Common;

namespace Stockly.Domain.Sales;

/// <summary>
/// One line of a <see cref="Sale"/>. Created only by the sale. <see cref="ListUnitPrice"/> is the unit's sell price at
/// sale time and <see cref="UnitPrice"/> the price charged (contract v1.1, S1); the line is "price overridden" when they
/// differ. <see cref="Quantity"/> is in the line's unit; the stock movement holds the base-unit change.
/// </summary>
public sealed class SaleItem : Entity, ITenantEntity
{
    private SaleItem(
        Guid shopId,
        Guid saleId,
        int lineNo,
        Guid productId,
        Guid productUnitId,
        decimal quantity,
        decimal listUnitPrice,
        decimal unitPrice,
        decimal lineTotal)
    {
        ShopId = shopId;
        SaleId = saleId;
        LineNo = lineNo;
        ProductId = productId;
        ProductUnitId = productUnitId;
        Quantity = quantity;
        ListUnitPrice = listUnitPrice;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
    }

    public Guid ShopId { get; }

    public Guid SaleId { get; }

    /// <summary>1..100 in request order, unique per sale.</summary>
    public int LineNo { get; }

    public Guid ProductId { get; }

    public Guid ProductUnitId { get; }

    /// <summary>In the line's unit, greater than 0.</summary>
    public decimal Quantity { get; }

    /// <summary>The unit's sell price when the sale was recorded.</summary>
    public decimal ListUnitPrice { get; }

    /// <summary>The price charged for one unit.</summary>
    public decimal UnitPrice { get; }

    /// <summary><see cref="ComputeLineTotal"/> of quantity and unit price.</summary>
    public decimal LineTotal { get; }

    public bool PriceOverridden => UnitPrice != ListUnitPrice;

    /// <summary>
    /// <c>quantity × unitPrice</c> rounded to 2 decimals, midpoint away from zero (docs/design/backend.md §17.1 item 6):
    /// a 3-decimal quantity times a 2-decimal price can have 5 decimals.
    /// </summary>
    public static decimal ComputeLineTotal(decimal quantity, decimal unitPrice) =>
        decimal.Round(quantity * unitPrice, Precision.MoneyDecimals, MidpointRounding.AwayFromZero);

    internal static Result<SaleItem> Create(
        Guid shopId, Guid saleId, int lineNo, Guid productId, Guid productUnitId, decimal quantity, decimal listUnitPrice, decimal unitPrice)
    {
        var error = (quantity <= 0m ? SaleErrors.QuantityNotPositive : null)
            ?? Precision.ValidateQuantity(quantity, nameof(Quantity))
            ?? Precision.ValidateMoney(listUnitPrice, nameof(ListUnitPrice))
            ?? Precision.ValidateMoney(unitPrice, nameof(UnitPrice));
        if (error is not null)
        {
            return error;
        }

        var lineTotal = ComputeLineTotal(quantity, unitPrice);
        var totalError = Precision.ValidateMoney(lineTotal, nameof(LineTotal));
        if (totalError is not null)
        {
            return totalError;
        }

        return new SaleItem(shopId, saleId, lineNo, productId, productUnitId, quantity, listUnitPrice, unitPrice, lineTotal);
    }
}
