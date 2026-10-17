using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using Stockly.Infrastructure.Persistence.Configurations;

namespace Stockly.Infrastructure.Persistence.Stock;

/// <summary>
/// The only writer of <c>products.quantity</c> (AGENTS.md rule 1, ADR-0001). The product row is locked with
/// <c>SELECT ... FOR UPDATE</c> (restricted to the current shop) before it is read, so concurrent movements on one
/// product queue up and each sees the balance the previous one committed (rule 4: one product per call). The movement
/// is added to the same context, so it is saved in the command's transaction together with the new quantity. Confirm
/// and sale lock all their products at once with one ordered statement (<see cref="LockProductsAsync"/>); a receipt or a
/// sale movement is refused for a product this service did not lock and that is not new in this context.
/// </summary>
internal sealed class StockService(StocklyDbContext db, ICurrentTenant currentTenant, IClock clock) : IStockService
{
    private const string LockProductSql =
        "SELECT 1 FROM " + ProductConfiguration.TableName + " WHERE id = {0} AND shop_id = {1} FOR UPDATE";

    // PostgreSQL takes the row locks in ORDER BY order, so every path locks products in the same uuid order (rule 4).
    private const string LockProductsSql =
        "SELECT 1 FROM " + ProductConfiguration.TableName + " WHERE id = ANY({0}) AND shop_id = {1} ORDER BY id FOR UPDATE";

    private readonly HashSet<Guid> _locked = [];

    public async Task<Result<StockMovement>> AdjustAsync(
        Guid productId, Guid? unitId, decimal quantityChange, string? note, Guid userId, CancellationToken cancellationToken)
    {
        var product = await LockAsync(productId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var movement = product.AdjustStock(unitId, quantityChange, userId, note, clock.UtcNow);
        if (movement.IsSuccess)
        {
            db.StockMovements.Add(movement.Value);
        }

        return movement;
    }

    public async Task<Result<StockCountResult>> CountAsync(
        Guid productId, Guid? unitId, decimal countedQuantity, Guid userId, CancellationToken cancellationToken)
    {
        var product = await LockAsync(productId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var outcome = product.CountStock(unitId, countedQuantity, userId, clock.UtcNow);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        Track(outcome.Value);
        return new StockCountResult(outcome.Value, product.Quantity);
    }

    public Result<StockCountOutcome> RecordOpeningStock(Product newProduct, decimal openingQuantity, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(newProduct);

        var outcome = newProduct.RecordOpeningStock(openingQuantity, userId, clock.UtcNow);
        if (outcome.IsSuccess)
        {
            Track(outcome.Value);
        }

        return outcome;
    }

    public async Task<IReadOnlyDictionary<Guid, Product>> LockProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        EnsureTransaction();
        if (productIds.Count == 0 || currentTenant.ShopId is not { } shopId)
        {
            return new Dictionary<Guid, Product>();
        }

        var ids = productIds.Distinct().ToArray();
        await db.Database.ExecuteSqlRawAsync(LockProductsSql, [ids, shopId], cancellationToken);
        var products = await db.Products
            .Include(product => product.Units)
            .Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        _locked.UnionWith(products.Keys);
        return products;
    }

    public Result<StockMovement> ReceiveFromInvoice(
        Product product, decimal baseQuantity, Guid invoiceId, Guid invoiceItemId, Guid userId) =>
        Record(product, baseQuantity, MovementReason.InvoiceReceipt, MovementSource.FromInvoiceItem(invoiceId, invoiceItemId), userId);

    public Result<StockMovement> RecordSale(Product product, decimal baseQuantity, Guid saleId, Guid saleItemId, Guid userId) =>
        Record(product, -baseQuantity, MovementReason.Sale, MovementSource.FromSaleItem(saleId, saleItemId), userId);

    private Result<StockMovement> Record(Product product, decimal change, MovementReason reason, MovementSource source, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (!_locked.Contains(product.Id) && db.Entry(product).State != EntityState.Added)
        {
            throw new InvalidOperationException("Lock the product with LockProductsAsync before changing its stock.");
        }

        var movement = product.RecordMovement(change, reason, source, userId, note: null, clock.UtcNow);
        if (movement.IsSuccess)
        {
            db.StockMovements.Add(movement.Value);
        }

        return movement;
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Stock changes must run inside the command's transaction, or the row lock is released at once.");
        }
    }

    private void Track(StockCountOutcome outcome)
    {
        if (outcome.Movement is { } movement)
        {
            db.StockMovements.Add(movement);
        }
    }

    private async Task<Product?> LockAsync(Guid productId, CancellationToken cancellationToken)
    {
        EnsureTransaction();

        if (currentTenant.ShopId is not { } shopId)
        {
            return null;
        }

        // The lock is taken first; the read that follows (a new statement under READ COMMITTED) sees the latest balance.
        await db.Database.ExecuteSqlRawAsync(LockProductSql, [productId, shopId], cancellationToken);
        return await db.Products
            .Include(product => product.Units)
            .SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);
    }
}
