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
    // all in one statement (InvoiceDetailProjection), under the tenant filter.
    public Task<InvoiceDetail?> FindDetailAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        InvoiceDetailProjection.FindAsync(
            db.Set<Invoice>().AsNoTracking(), db.Products.AsNoTracking(), db.Set<ProductUnit>().AsNoTracking(), invoiceId, cancellationToken);

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
}
