using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Common;
using Stockly.Contracts.Sales;
using Stockly.Domain.Products;
using Stockly.Domain.Sales;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>Sale reads: projections, no tracking, tenant-filtered (AGENTS.md rule 33). Indexes in docs/erd.md §3.4.</summary>
internal sealed class SaleQueries(StocklyDbContext db) : ISaleQueries
{
    // S1/S3: primary key; S1 repeat: ux_sales_shop_id_client_request_id.
    public Task<SaleDto?> FindAsync(Guid saleId, CancellationToken cancellationToken) =>
        FindOneAsync(sale => sale.Id == saleId, cancellationToken);

    public Task<SaleDto?> FindByClientRequestIdAsync(Guid clientRequestId, CancellationToken cancellationToken) =>
        FindOneAsync(sale => sale.ClientRequestId == clientRequestId, cancellationToken);

    // S2: ix_sales_shop_id_created_at (shop_id, created_at DESC), the range on created_at; the item count is a correlated
    // COUNT served by ux_sale_items_sale_id_line_no. Newest first (ADR-0010: never by id; id only breaks ties).
    public async Task<PagedResponse<SaleListItem>> ListAsync(
        DateTimeOffset? createdFrom, DateTimeOffset? createdBefore, int page, int pageSize, CancellationToken cancellationToken)
    {
        var sales = db.Set<Sale>().AsNoTracking();
        if (createdFrom is { } from)
        {
            sales = sales.Where(sale => sale.CreatedAt >= from);
        }

        if (createdBefore is { } before)
        {
            sales = sales.Where(sale => sale.CreatedAt < before);
        }

        var totalCount = await sales.CountAsync(cancellationToken);
        var rows = await sales
            .OrderByDescending(sale => sale.CreatedAt)
            .ThenByDescending(sale => sale.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sale => new { sale.Id, sale.TotalAmount, ItemCount = sale.Items.Count, sale.CreatedAt })
            .ToListAsync(cancellationToken);

        return new PagedResponse<SaleListItem>(
            [.. rows.Select(row => new SaleListItem(row.Id, ProductMapping.Money(row.TotalAmount), row.ItemCount, row.CreatedAt.UtcDateTime))],
            page,
            pageSize,
            totalCount);
    }

    // One statement: the sale, its lines by ux_sale_items_sale_id_line_no, product and unit names by primary key, and
    // each line's balance_after through ux_stock_movements_sale_item_id.
    private async Task<SaleDto?> FindOneAsync(Expression<Func<Sale, bool>> predicate, CancellationToken cancellationToken)
    {
        var row = await db.Set<Sale>().AsNoTracking()
            .Where(predicate)
            .Select(sale => new
            {
                sale.Id,
                sale.ClientRequestId,
                sale.TotalAmount,
                sale.Note,
                sale.CreatedAt,
                sale.CreatedByUserId,
                Items = sale.Items
                    .OrderBy(item => item.LineNo)
                    .Select(item => new ItemRow(
                        item.Id,
                        item.ProductId,
                        db.Products.Where(product => product.Id == item.ProductId).Select(product => product.Name).First(),
                        item.ProductUnitId,
                        db.Set<ProductUnit>().Where(unit => unit.Id == item.ProductUnitId).Select(unit => unit.Name).First(),
                        item.Quantity,
                        item.ListUnitPrice,
                        item.UnitPrice,
                        item.LineTotal,
                        db.StockMovements.Where(movement => movement.SaleItemId == item.Id).Select(movement => movement.BalanceAfter).First()))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // A product's stock after this sale is the balance after its last line.
        var warnings = row.Items
            .GroupBy(item => item.ProductId)
            .Where(group => group.Last().BalanceAfter < 0m)
            .Select(group => new SaleWarningDto(SaleWarningCodes.NegativeBalance, group.Key))
            .ToList();
        var items = row.Items.Select(ToDto).ToList();
        return new SaleDto(
            row.Id,
            row.ClientRequestId,
            ProductMapping.Money(row.TotalAmount),
            row.Note,
            items.Exists(item => item.PriceOverridden),
            items,
            row.CreatedAt.UtcDateTime,
            row.CreatedByUserId,
            warnings);
    }

    private static SaleItemDto ToDto(ItemRow row) => new(
        row.Id,
        row.ProductId,
        row.ProductName,
        row.ProductUnitId,
        row.UnitName,
        ProductMapping.Quantity(row.Quantity),
        ProductMapping.Money(row.ListUnitPrice),
        ProductMapping.Money(row.UnitPrice),
        row.UnitPrice != row.ListUnitPrice,
        ProductMapping.Money(row.LineTotal),
        ProductMapping.Quantity(row.BalanceAfter));

    private sealed record ItemRow(
        Guid Id,
        Guid ProductId,
        string ProductName,
        Guid ProductUnitId,
        string UnitName,
        decimal Quantity,
        decimal ListUnitPrice,
        decimal UnitPrice,
        decimal LineTotal,
        decimal BalanceAfter);
}
