using Stockly.Contracts.Common;
using Stockly.Contracts.Sales;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Read-only projections of the current shop's sales (tenant-filtered, no tracking, AGENTS.md rule 33).</summary>
public interface ISaleQueries
{
    /// <summary>The sale with its lines in order (S1, S3); <c>null</c> when missing or another shop's. One query.</summary>
    Task<SaleDto?> FindAsync(Guid saleId, CancellationToken cancellationToken);

    /// <summary>The sale recorded with this client request id (ADR-0006); <c>null</c> when none. One query.</summary>
    Task<SaleDto?> FindByClientRequestIdAsync(Guid clientRequestId, CancellationToken cancellationToken);

    /// <summary>
    /// A page of sales, newest first (S2), created at or after <paramref name="createdFrom"/> and before
    /// <paramref name="createdBefore"/> when given (UTC). The item count is in the same query.
    /// </summary>
    Task<PagedResponse<SaleListItem>> ListAsync(
        DateTimeOffset? createdFrom, DateTimeOffset? createdBefore, int page, int pageSize, CancellationToken cancellationToken);
}
