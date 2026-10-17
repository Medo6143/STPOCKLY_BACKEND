using Stockly.Domain.Invoices;

namespace Stockly.Application.Abstractions.Caching;

/// <summary>
/// The invoice status for polling (<c>stockly:{shopId}:invoice:{id}:status</c>, 10 minutes, docs/design/redis.md).
/// Written by the scan, the job and confirm after their commit, and read-through by I3 for a processing invoice;
/// overwritten on every change. Redis failures are misses
/// and skipped writes, never errors (rule 25).
/// </summary>
public interface IInvoiceStatusCache
{
    Task<InvoiceStatusSnapshot?> GetAsync(Guid shopId, Guid invoiceId, CancellationToken cancellationToken);

    Task SetAsync(Guid shopId, InvoiceStatusSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>
/// What a poll needs without PostgreSQL (docs/design/backend.md §17.1 item 5): status, when it last changed, and the
/// invoice's client request id and creation time.
/// </summary>
public sealed record InvoiceStatusSnapshot(
    Guid InvoiceId, InvoiceStatus Status, DateTimeOffset UpdatedAt, Guid ClientRequestId, DateTimeOffset CreatedAt);
