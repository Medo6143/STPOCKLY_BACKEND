using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;
using DomainInvoiceWarning = Stockly.Domain.Invoices.InvoiceWarning;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>Invoice reads: projections, no tracking, tenant-filtered (AGENTS.md rule 33). Indexes in docs/erd.md §3.3.</summary>
internal sealed class InvoiceQueries(StocklyDbContext db) : IInvoiceQueries
{
    // The list row; the item count is a correlated COUNT in the same statement (ux_invoice_items_invoice_id_line_no).
    private static readonly Expression<Func<Invoice, ListRow>> ToListRow = invoice => new ListRow(
        invoice.Id,
        invoice.Status,
        invoice.SupplierNameRaw,
        invoice.InvoiceNumber,
        invoice.InvoiceDate,
        invoice.TotalAmount,
        invoice.Items.Count,
        invoice.Warnings,
        invoice.CreatedAt);

    // ux_invoices_shop_id_client_request_id.
    public async Task<InvoiceListItem?> FindListItemByClientRequestIdAsync(Guid clientRequestId, CancellationToken cancellationToken)
    {
        var row = await db.Set<Invoice>().AsNoTracking()
            .Where(invoice => invoice.ClientRequestId == clientRequestId)
            .Select(ToListRow)
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : ToListItem(row);
    }

    // I2. Without a status: ix_invoices_shop_id_created_at (shop_id, created_at DESC). With a status:
    // ix_invoices_shop_id_status_created_at (shop_id, status, created_at DESC). Newest first (ADR-0010: never by id).
    public async Task<PagedResponse<InvoiceListItem>> ListAsync(
        DomainInvoiceStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var invoices = db.Set<Invoice>().AsNoTracking();
        if (status is { } wanted)
        {
            invoices = invoices.Where(invoice => invoice.Status == wanted);
        }

        var totalCount = await invoices.CountAsync(cancellationToken);
        var rows = await invoices
            .OrderByDescending(invoice => invoice.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToListRow)
            .ToListAsync(cancellationToken);

        return new PagedResponse<InvoiceListItem>([.. rows.Select(ToListItem)], page, pageSize, totalCount);
    }

    // I3. Primary key; lines by ux_invoice_items_invoice_id_line_no in line order; product and unit names by primary key,
    // all in one statement. A confirmed new-product line names the product created from it (created_product_id).
    public async Task<InvoiceDetail?> FindDetailAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var row = await db.Set<Invoice>().AsNoTracking()
            .Where(invoice => invoice.Id == invoiceId)
            .Select(invoice => new
            {
                invoice.Id,
                invoice.ClientRequestId,
                invoice.Status,
                invoice.SupplierId,
                invoice.SupplierNameRaw,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.TotalAmount,
                invoice.ComputedTotal,
                invoice.Warnings,
                invoice.FailureReason,
                invoice.CreatedAt,
                invoice.ProcessedAt,
                invoice.ConfirmedAt,
                Items = invoice.Items
                    .OrderBy(item => item.LineNo)
                    .Select(item => new ItemRow(
                        item.Id,
                        item.LineNo,
                        item.RawName,
                        item.ProductId ?? item.CreatedProductId,
                        db.Products
                            .Where(product => product.Id == (item.ProductId ?? item.CreatedProductId))
                            .Select(product => product.Name)
                            .FirstOrDefault(),
                        item.ProductUnitId,
                        db.Set<ProductUnit>()
                            .Where(unit => unit.Id == item.ProductUnitId)
                            .Select(unit => unit.Name)
                            .FirstOrDefault(),
                        item.Quantity,
                        item.UnitPrice,
                        item.LineTotal,
                        item.ExpiryDate,
                        item.BatchNo,
                        item.MatchConfidence,
                        item.NewProductName,
                        item.NewProductBaseUnitName,
                        item.NewProductSellPrice))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new InvoiceDetail(
                row.Id,
                row.ClientRequestId,
                InvoiceMapping.ToContract(row.Status),
                row.SupplierId,
                row.SupplierNameRaw,
                row.InvoiceNumber,
                row.InvoiceDate,
                Money(row.TotalAmount),
                Money(row.ComputedTotal),
                [.. row.Warnings.Select(InvoiceMapping.ToContract)],
                row.FailureReason?.ToString(),
                [.. row.Items.Select(ToDto)],
                row.CreatedAt.UtcDateTime,
                row.ProcessedAt?.UtcDateTime,
                row.ConfirmedAt?.UtcDateTime);
    }

    private static InvoiceListItem ToListItem(ListRow row) => new(
        row.Id,
        InvoiceMapping.ToContract(row.Status),
        row.SupplierNameRaw,
        row.InvoiceNumber,
        row.InvoiceDate,
        Money(row.TotalAmount),
        row.ItemCount,
        [.. row.Warnings.Select(InvoiceMapping.ToContract)],
        row.CreatedAt.UtcDateTime);

    private static InvoiceItemDto ToDto(ItemRow row) => new(
        row.Id,
        row.LineNo,
        row.RawName,
        row.ProductId,
        row.ProductName,
        row.ProductUnitId,
        row.UnitName,
        ProductMapping.Quantity(row.Quantity),
        ProductMapping.Money(row.UnitPrice),
        ProductMapping.Money(row.LineTotal),
        row.ExpiryDate,
        row.BatchNo,
        row.MatchConfidence is { } confidence ? InvoiceMapping.Confidence(confidence) : null,
        row is { NewProductName: { } name, NewProductBaseUnitName: { } baseUnit, NewProductSellPrice: { } sellPrice }
            ? new InvoiceNewProductDto(name, baseUnit, ProductMapping.Money(sellPrice))
            : null);

    private static string? Money(decimal? value) => value is { } amount ? ProductMapping.Money(amount) : null;

    private sealed record ListRow(
        Guid Id,
        DomainInvoiceStatus Status,
        string? SupplierNameRaw,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        decimal? TotalAmount,
        int ItemCount,
        IReadOnlyList<DomainInvoiceWarning> Warnings,
        DateTimeOffset CreatedAt);

    private sealed record ItemRow(
        Guid Id,
        int LineNo,
        string RawName,
        Guid? ProductId,
        string? ProductName,
        Guid? ProductUnitId,
        string? UnitName,
        decimal Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        DateOnly? ExpiryDate,
        string? BatchNo,
        decimal? MatchConfidence,
        string? NewProductName,
        string? NewProductBaseUnitName,
        decimal? NewProductSellPrice);
}
