using Stockly.Contracts.Invoices;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Read-only projections of the current shop's invoices (tenant-filtered, no tracking, AGENTS.md rule 33).</summary>
public interface IInvoiceQueries
{
    /// <summary>The invoice created by the scan with this client request id (ADR-0006); <c>null</c> when none.</summary>
    Task<InvoiceListItem?> FindListItemByClientRequestIdAsync(Guid clientRequestId, CancellationToken cancellationToken);
}
