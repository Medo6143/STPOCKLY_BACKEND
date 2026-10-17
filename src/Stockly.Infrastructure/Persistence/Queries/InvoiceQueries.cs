using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>Invoice reads: projections, no tracking, tenant-filtered (AGENTS.md rule 33). Indexes in docs/erd.md §3.3.</summary>
internal sealed class InvoiceQueries(StocklyDbContext db) : IInvoiceQueries
{
    // ux_invoices_shop_id_client_request_id; the item count is a correlated COUNT in the same statement.
    public async Task<InvoiceListItem?> FindListItemByClientRequestIdAsync(Guid clientRequestId, CancellationToken cancellationToken)
    {
        var row = await db.Set<Invoice>().AsNoTracking()
            .Where(invoice => invoice.ClientRequestId == clientRequestId)
            .Select(invoice => new
            {
                invoice.Id,
                invoice.Status,
                invoice.SupplierNameRaw,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.TotalAmount,
                ItemCount = invoice.Items.Count,
                invoice.Warnings,
                invoice.CreatedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new InvoiceListItem(
                row.Id,
                InvoiceMapping.ToContract(row.Status),
                row.SupplierNameRaw,
                row.InvoiceNumber,
                row.InvoiceDate,
                row.TotalAmount is { } total ? ProductMapping.Money(total) : null,
                row.ItemCount,
                [.. row.Warnings.Select(InvoiceMapping.ToContract)],
                row.CreatedAt.UtcDateTime);
    }
}
