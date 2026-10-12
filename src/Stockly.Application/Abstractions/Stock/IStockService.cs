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
}

/// <summary>A count's outcome and the product quantity after it (base unit).</summary>
public sealed record StockCountResult(StockCountOutcome Outcome, decimal Quantity);
