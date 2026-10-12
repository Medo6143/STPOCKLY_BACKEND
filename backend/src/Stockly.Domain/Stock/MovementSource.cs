namespace Stockly.Domain.Stock;

/// <summary>
/// What caused a movement: nothing, one invoice line, or one sale line. The factories make "two sources"
/// impossible to build, mirroring <c>CHECK (num_nonnulls(invoice_item_id, sale_item_id) &lt;= 1)</c>.
/// </summary>
public sealed record MovementSource
{
    private MovementSource(Guid? invoiceId, Guid? invoiceItemId, Guid? saleId, Guid? saleItemId)
    {
        InvoiceId = invoiceId;
        InvoiceItemId = invoiceItemId;
        SaleId = saleId;
        SaleItemId = saleItemId;
    }

    public static MovementSource None { get; } = new(null, null, null, null);

    public Guid? InvoiceId { get; }

    public Guid? InvoiceItemId { get; }

    public Guid? SaleId { get; }

    public Guid? SaleItemId { get; }

    internal bool IsInvoiceItem => InvoiceItemId is not null;

    internal bool IsSaleItem => SaleItemId is not null;

    internal bool IsNone => !IsInvoiceItem && !IsSaleItem;

    internal bool HasEmptyReference =>
        InvoiceId == Guid.Empty || InvoiceItemId == Guid.Empty || SaleId == Guid.Empty || SaleItemId == Guid.Empty;

    /// <summary>For <see cref="MovementReason.InvoiceReceipt"/> and <see cref="MovementReason.InvoiceReversal"/>.</summary>
    public static MovementSource FromInvoiceItem(Guid invoiceId, Guid invoiceItemId) =>
        new(invoiceId, invoiceItemId, null, null);

    /// <summary>For <see cref="MovementReason.Sale"/>.</summary>
    public static MovementSource FromSaleItem(Guid saleId, Guid saleItemId) =>
        new(null, null, saleId, saleItemId);
}
