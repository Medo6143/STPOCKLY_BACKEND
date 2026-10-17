using Stockly.Contracts.Common;
using Stockly.Contracts.Dashboard;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Read-only dashboard figures of the current shop (tenant-filtered, no tracking, AGENTS.md rule 33).</summary>
public interface IDashboardQueries
{
    /// <summary>
    /// The D1 counts: products, low and negative stock (non-archived), expiring lines (as <see cref="ListExpiringAsync"/>
    /// up to <paramref name="expiringUntil"/>), sales created in [<paramref name="dayStart"/>, <paramref name="dayEnd"/>)
    /// and invoices waiting for review.
    /// </summary>
    Task<DashboardCounts> GetCountsAsync(
        DateTimeOffset dayStart, DateTimeOffset dayEnd, DateOnly expiringUntil, CancellationToken cancellationToken);

    /// <summary>
    /// D2: lines of confirmed invoices with an expiry date on or before <paramref name="expiringUntil"/> whose product is
    /// not archived and still has stock (quantity &gt; 0), soonest first.
    /// </summary>
    Task<PagedResponse<ExpiringItem>> ListExpiringAsync(
        DateOnly expiringUntil, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>The D1 numbers before formatting.</summary>
public sealed record DashboardCounts(
    int ProductCount,
    int LowStockCount,
    int NegativeStockCount,
    int ExpiringSoonCount,
    int TodaySalesCount,
    decimal TodaySalesTotal,
    int TodayOverriddenSalesCount,
    int InvoicesAwaitingReview);
