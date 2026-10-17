using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;

namespace Stockly.Application.Abstractions.Stock;

/// <summary>
/// The only path that changes <c>Product.Quantity</c> (AGENTS.md rule 1, ADR-0001). Each method locks the product row
/// (<c>SELECT ... FOR UPDATE</c>, tenant-filtered), calls the Domain and adds the new movement, inside the command's
/// transaction. The caller saves through <c>IUnitOfWork</c>.
/// </summary>
public interface IStockService
{
    /// <summary>A manual adjustment (P9) in the given unit (<c>null</c> = base unit). <c>Product.NotFound</c> when missing.</summary>
    Task<Result<StockMovement>> AdjustAsync(
        Guid productId, Guid? unitId, decimal quantityChange, string? note, Guid userId, CancellationToken cancellationToken);

    /// <summary>A stock count (P10) in the given unit (<c>null</c> = base unit). <c>Product.NotFound</c> when missing.</summary>
    Task<Result<StockCountResult>> CountAsync(
        Guid productId, Guid? unitId, decimal countedQuantity, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The opening stock of a product created in this transaction (P3). Nothing else can see the new row, so there is
    /// nothing to lock. Zero records nothing.
    /// </summary>
    Result<StockCountOutcome> RecordOpeningStock(Product newProduct, decimal openingQuantity, Guid userId);

    /// <summary>
    /// Locks the current shop's products with these ids in one statement, <c>SELECT ... WHERE id = ANY(@ids) ORDER BY id
    /// FOR UPDATE</c>, so every path takes product locks in the same (PostgreSQL uuid) order (AGENTS.md rule 4), then
    /// loads them tracked with their units. Call it after locking the invoice or the sale, inside the transaction. Missing
    /// and other shops' ids are absent from the result. Quantities come from these rows, never from cache (rule 24).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Product>> LockProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    /// <summary>
    /// An <c>InvoiceReceipt</c> of <paramref name="baseQuantity"/> (base unit, &gt; 0) into a product locked by
    /// <see cref="LockProductsAsync"/> or created in this transaction, from one invoice line (confirm, I5).
    /// </summary>
    Result<StockMovement> ReceiveFromInvoice(Product product, decimal baseQuantity, Guid invoiceId, Guid invoiceItemId, Guid userId);

    /// <summary>
    /// A <c>Sale</c> movement of <c>-<paramref name="baseQuantity"/></c> (base unit) from a product locked by
    /// <see cref="LockProductsAsync"/>, for one sale line (S1). Negative stock is allowed (contract §7).
    /// </summary>
    Result<StockMovement> RecordSale(Product product, decimal baseQuantity, Guid saleId, Guid saleItemId, Guid userId);
}

/// <summary>A count's outcome and the product quantity after it (base unit).</summary>
public sealed record StockCountResult(StockCountOutcome Outcome, decimal Quantity);
