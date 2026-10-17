using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Invoices;

/// <summary>
/// One line of an <see cref="Invoice"/> (docs/design/backend.md §10). Created only by the invoice. A line from the AI
/// references no product yet; the review edit and confirm (later steps) fill the product columns.
/// </summary>
public sealed class InvoiceItem : Entity, ITenantEntity
{
    private InvoiceItem(
        Guid shopId,
        Guid invoiceId,
        int lineNo,
        string rawName,
        decimal quantity,
        decimal unitPrice,
        decimal lineTotal,
        DateOnly? expiryDate,
        string? batchNo)
    {
        ShopId = shopId;
        InvoiceId = invoiceId;
        LineNo = lineNo;
        RawName = rawName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
        ExpiryDate = expiryDate;
        BatchNo = batchNo;
    }

    public Guid ShopId { get; }

    public Guid InvoiceId { get; }

    /// <summary>1..100, unique per invoice.</summary>
    public int LineNo { get; }

    /// <summary>The product name exactly as written on the invoice (trimmed).</summary>
    public string RawName { get; }

    /// <summary>Existing product the line receives into; <c>null</c> while unmatched.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>Unit of <see cref="Quantity"/> for an existing product; set together with <see cref="ProductId"/>.</summary>
    public Guid? ProductUnitId { get; private set; }

    /// <summary>A new product to create on confirm (review edit, contract I4).</summary>
    public string? NewProductName { get; private set; }

    public string? NewProductBaseUnitName { get; private set; }

    public decimal? NewProductSellPrice { get; private set; }

    /// <summary>The product created on confirm from the <c>NewProduct*</c> fields.</summary>
    public Guid? CreatedProductId { get; private set; }

    /// <summary>In the line's unit, greater than 0.</summary>
    public decimal Quantity { get; }

    /// <summary>Price of one unit as written.</summary>
    public decimal UnitPrice { get; }

    /// <summary><see cref="ComputeLineTotal"/> of quantity and unit price.</summary>
    public decimal LineTotal { get; }

    /// <summary>Informational only (ADR-0004).</summary>
    public DateOnly? ExpiryDate { get; }

    /// <summary>Informational only (ADR-0004).</summary>
    public string? BatchNo { get; }

    /// <summary>0.00–1.00 when a product was matched automatically.</summary>
    public decimal? MatchConfidence { get; private set; }

    /// <summary>The invoice's confirmation time, set on every line by confirm.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>
    /// <c>quantity × unitPrice</c> rounded to 2 decimals, midpoint away from zero (docs/design/backend.md §17.1 item 6):
    /// a 3-decimal quantity times a 2-decimal price can have 5 decimals.
    /// </summary>
    public static decimal ComputeLineTotal(decimal quantity, decimal unitPrice) =>
        decimal.Round(quantity * unitPrice, Precision.MoneyDecimals, MidpointRounding.AwayFromZero);

    internal static Result<InvoiceItem> FromDraft(Guid shopId, Guid invoiceId, int lineNo, InvoiceLineDraft draft)
    {
        if (lineNo is < 1 or > InvoiceLimits.MaxLines)
        {
            return InvoiceErrors.TooManyLines;
        }

        var rawName = draft.RawName.Trim();
        var error = (ArabicTextNormalizer.Normalize(rawName).Length == 0 ? InvoiceErrors.LineNameRequired : null)
            ?? (draft.Quantity <= 0m ? InvoiceErrors.LineQuantityNotPositive : null)
            ?? Precision.ValidateQuantity(draft.Quantity, nameof(draft.Quantity))
            ?? Precision.ValidateMoney(draft.UnitPrice, nameof(draft.UnitPrice));
        if (error is not null)
        {
            return error;
        }

        var lineTotal = ComputeLineTotal(draft.Quantity, draft.UnitPrice);
        var totalError = Precision.ValidateMoney(lineTotal, nameof(LineTotal));
        if (totalError is not null)
        {
            return totalError;
        }

        return new InvoiceItem(
            shopId, invoiceId, lineNo, rawName, draft.Quantity, draft.UnitPrice, lineTotal, draft.ExpiryDate, OptionalText.Clean(draft.BatchNo));
    }
}
