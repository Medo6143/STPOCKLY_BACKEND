using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Common;
using Stockly.Contracts.Dashboard;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using Stockly.Domain.Sales;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>Dashboard reads (D1, D2): projections, no tracking, tenant-filtered (AGENTS.md rule 33). Indexes in docs/erd.md.</summary>
internal sealed class DashboardQueries(StocklyDbContext db) : IDashboardQueries
{
    public async Task<DashboardCounts> GetCountsAsync(
        DateTimeOffset dayStart, DateTimeOffset dayEnd, DateOnly expiringUntil, CancellationToken cancellationToken)
    {
        var active = db.Products.AsNoTracking().Where(product => !product.IsArchived);

        // ix_products_shop_id_normalized_name (shop_id, normalized_name) WHERE NOT is_archived.
        var productCount = await active.CountAsync(cancellationToken);

        // ix_products_low_stock (shop_id) WHERE quantity <= low_stock_threshold AND NOT is_archived. The negative count
        // repeats the index predicate (a negative quantity is always low, thresholds are >= 0) so the index serves it too.
        var lowStock = active.Where(product => product.Quantity <= product.LowStockThreshold);
        var lowStockCount = await lowStock.CountAsync(cancellationToken);
        var negativeStockCount = await lowStock.CountAsync(product => product.Quantity < 0m, cancellationToken);

        var expiringSoonCount = await Expiring(expiringUntil).CountAsync(cancellationToken);

        // ix_sales_shop_id_created_at; the override check reads the lines through ux_sale_items_sale_id_line_no.
        var today = await db.Set<Sale>().AsNoTracking()
            .Where(sale => sale.CreatedAt >= dayStart && sale.CreatedAt < dayEnd)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Total = group.Sum(sale => sale.TotalAmount),
                Overridden = group.Count(sale => sale.Items.Any(item => item.UnitPrice != item.ListUnitPrice)),
            })
            .SingleOrDefaultAsync(cancellationToken);

        // ix_invoices_shop_id_status_created_at (shop_id, status, created_at DESC).
        var awaitingReview = await db.Set<Invoice>().AsNoTracking()
            .CountAsync(invoice => invoice.Status == InvoiceStatus.NeedsReview, cancellationToken);

        return new DashboardCounts(
            productCount,
            lowStockCount,
            negativeStockCount,
            expiringSoonCount,
            today?.Count ?? 0,
            today?.Total ?? 0m,
            today?.Overridden ?? 0,
            awaitingReview);
    }

    public async Task<PagedResponse<ExpiringItem>> ListExpiringAsync(
        DateOnly expiringUntil, int page, int pageSize, CancellationToken cancellationToken)
    {
        var lines = Expiring(expiringUntil);
        var totalCount = await lines.CountAsync(cancellationToken);
        var rows = await lines
            .OrderBy(line => line.ExpiryDate)
            .ThenBy(line => line.ProductName)
            .ThenBy(line => line.LineId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ExpiringItem(
                row.ProductId,
                row.ProductName,
                row.ExpiryDate,
                row.BatchNo,
                row.InvoiceId,
                ProductMapping.Quantity(ToBase(row.LineQuantity, row.Factor)),
                ProductMapping.Quantity(row.CurrentQuantity)))
            .ToList();
        return new PagedResponse<ExpiringItem>(items, page, pageSize, totalCount);
    }

    // ix_invoice_items_shop_id_expiry_date (shop_id, expiry_date) WHERE expiry_date IS NOT NULL; the line's product is
    // COALESCE(product_id, created_product_id) (backend.md §10.6), joined by primary key; confirmed lines only.
    private IQueryable<ExpiringRow> Expiring(DateOnly expiringUntil) =>
        db.Set<InvoiceItem>().AsNoTracking()
            .Where(line => line.ConfirmedAt != null && line.ExpiryDate != null && line.ExpiryDate <= expiringUntil)
            .Join(
                db.Products.AsNoTracking().Where(product => !product.IsArchived && product.Quantity > 0m),
                line => line.ProductId ?? line.CreatedProductId,
                product => (Guid?)product.Id,
                (line, product) => new ExpiringRow
                {
                    LineId = line.Id,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    ExpiryDate = line.ExpiryDate!.Value,
                    BatchNo = line.BatchNo,
                    InvoiceId = line.InvoiceId,
                    LineQuantity = line.Quantity,
                    Factor = db.Set<ProductUnit>()
                        .Where(unit => unit.Id == line.ProductUnitId)
                        .Select(unit => (decimal?)unit.Factor)
                        .FirstOrDefault(),
                    CurrentQuantity = product.Quantity,
                });

    // The line's quantity in base units (ProductUnit.ToBase; a new product's line is already in its base unit).
    private static decimal ToBase(decimal quantity, decimal? factor) =>
        decimal.Round(quantity * (factor ?? 1m), Precision.QuantityDecimals, MidpointRounding.AwayFromZero);

    // A class with settable properties (not a positional record), so EF can translate ordering on its members.
    private sealed class ExpiringRow
    {
        public Guid LineId { get; init; }

        public Guid ProductId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public DateOnly ExpiryDate { get; init; }

        public string? BatchNo { get; init; }

        public Guid InvoiceId { get; init; }

        public decimal LineQuantity { get; init; }

        public decimal? Factor { get; init; }

        public decimal CurrentQuantity { get; init; }
    }
}
