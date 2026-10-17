using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Read-only projections of the current shop's invoices (tenant-filtered, no tracking, AGENTS.md rule 33).</summary>
public interface IInvoiceQueries
{
    /// <summary>The invoice created by the scan with this client request id (ADR-0006); <c>null</c> when none.</summary>
    Task<InvoiceListItem?> FindListItemByClientRequestIdAsync(Guid clientRequestId, CancellationToken cancellationToken);

    /// <summary>The invoice with its lines by line number (I3); <c>null</c> when missing or another shop's. One query.</summary>
    Task<InvoiceDetail?> FindDetailAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>A page of invoices, newest first, optionally of one status (I2). The item count is in the same query.</summary>
    Task<PagedResponse<InvoiceListItem>> ListAsync(
        Domain.Invoices.InvoiceStatus? status, int page, int pageSize, CancellationToken cancellationToken);
}
