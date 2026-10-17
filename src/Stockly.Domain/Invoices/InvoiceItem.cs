using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Invoices;

/// <summary>
/// One line of an <see cref="Invoice"/> (docs/design/backend.md §10). Created only by the invoice. A line from the AI
/// references a matched product or nothing; a line of the review edit references an existing product or describes a new
/// one; confirm (Sprint 4) fills the created product.
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

    /// <summary>Decimals of <see cref="MatchConfidence"/> (contract §1.2, <c>numeric(3,2)</c>).</summary>
    public const int MatchConfidenceDecimals = 2;

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

    /// <summary>A line of the extraction, matched to a product when <see cref="InvoiceLineDraft.Match"/> is set.</summary>
    internal static Result<InvoiceItem> FromDraft(Guid shopId, Guid invoiceId, int lineNo, InvoiceLineDraft draft)
    {
        if (lineNo is < 1 or > InvoiceLimits.MaxLines)
        {
            return InvoiceErrors.TooManyLines;
        }

        if (draft.Match is { } match && !IsValidConfidence(match.Confidence))
        {
            return InvoiceErrors.MatchConfidenceInvalid;
        }

        var created = Create(shopId, invoiceId, lineNo, draft.RawName, draft.Quantity, draft.UnitPrice, draft.ExpiryDate, draft.BatchNo);
        if (created.IsFailure || draft.Match is null)
        {
            return created;
        }

        var item = created.Value;
        item.ProductId = draft.Match.ProductId;
        item.ProductUnitId = draft.Match.ProductUnitId;
        item.MatchConfidence = draft.Match.Confidence;
        return item;
    }

    /// <summary>
    /// A line of the review edit (contract I4): an existing product with its unit, or a new product, never both or
    /// neither. <paramref name="matchConfidence"/> is the kept confidence of an unchanged match, else <c>null</c>.
    /// </summary>
    internal static Result<InvoiceItem> FromReview(Guid shopId, Guid invoiceId, InvoiceReviewLine line, decimal? matchConfidence)
    {
        if (line.LineNo is < 1 or > InvoiceLimits.MaxLines)
        {
            return InvoiceErrors.LineNumberInvalid;
        }

        var error = (line.Product, line.NewProduct) switch
        {
            (not null, not null) => InvoiceErrors.LineProductConflict,
            (null, null) => InvoiceErrors.LineProductRequired,
            (null, { } newProduct) => ValidateNewProduct(newProduct),
            _ => matchConfidence is { } confidence && !IsValidConfidence(confidence) ? InvoiceErrors.MatchConfidenceInvalid : null,
        };
        if (error is not null)
        {
            return error;
        }

        var created = Create(shopId, invoiceId, line.LineNo, line.RawName, line.Quantity, line.UnitPrice, line.ExpiryDate, line.BatchNo);
        if (created.IsFailure)
        {
            return created;
        }

        var item = created.Value;
        if (line.Product is { } product)
        {
            item.ProductId = product.ProductId;
            item.ProductUnitId = product.ProductUnitId;
            item.MatchConfidence = matchConfidence;
        }
        else
        {
            var newProduct = line.NewProduct!;
            item.NewProductName = newProduct.Name.Trim();
            item.NewProductBaseUnitName = newProduct.BaseUnitName.Trim();
            item.NewProductSellPrice = newProduct.SellPrice;
        }

        return item;
    }

    private static Result<InvoiceItem> Create(
        Guid shopId, Guid invoiceId, int lineNo, string rawName, decimal quantity, decimal unitPrice, DateOnly? expiryDate, string? batchNo)
    {
        var name = rawName.Trim();
        var error = (ArabicTextNormalizer.Normalize(name).Length == 0 ? InvoiceErrors.LineNameRequired : null)
            ?? (quantity <= 0m ? InvoiceErrors.LineQuantityNotPositive : null)
            ?? Precision.ValidateQuantity(quantity, nameof(Quantity))
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

        return new InvoiceItem(shopId, invoiceId, lineNo, name, quantity, unitPrice, lineTotal, expiryDate, OptionalText.Clean(batchNo));
    }

    private static Error? ValidateNewProduct(InvoiceNewProduct newProduct) =>
        ArabicTextNormalizer.Normalize(newProduct.Name).Length == 0 || ArabicTextNormalizer.Normalize(newProduct.BaseUnitName).Length == 0
            ? InvoiceErrors.NewProductIncomplete
            : Precision.ValidateMoney(newProduct.SellPrice, nameof(NewProductSellPrice));

    private static bool IsValidConfidence(decimal confidence) =>
        confidence is >= 0m and <= 1m && decimal.Round(confidence, MatchConfidenceDecimals) == confidence;
}
