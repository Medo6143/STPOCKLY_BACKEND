using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Invoices;
using Stockly.Domain.Sales;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>P8 in-use checks: one <c>EXISTS</c> each, tenant-filtered, no tracking (docs/erd.md §3.3, §3.4).</summary>
internal sealed class ProductUnitUsageQueries(StocklyDbContext db) : IProductUnitUsageQueries
{
    // ix_invoice_items_product_unit_id (FK support index).
    public Task<bool> IsUsedByInvoiceLinesAsync(Guid unitId, CancellationToken cancellationToken) =>
        db.Set<InvoiceItem>().AsNoTracking().AnyAsync(item => item.ProductUnitId == unitId, cancellationToken);

    // ix_sale_items_product_unit_id (FK support index).
    public Task<bool> IsUsedBySaleLinesAsync(Guid unitId, CancellationToken cancellationToken) =>
        db.Set<SaleItem>().AsNoTracking().AnyAsync(item => item.ProductUnitId == unitId, cancellationToken);
}
