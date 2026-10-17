using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Dashboard;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Dashboard.GetDashboardSummary;

/// <summary>
/// D1 (contract §8), cache-aside on <c>stockly:{shopId}:dashboard:summary:v{ver}:{yyyyMMdd}</c> for 2 minutes
/// (docs/design/redis.md). "Today" is the Africa/Cairo day from <see cref="IClock"/>. Every stock, product and sale write
/// bumps the catalog version after its commit, so a write is visible on the next read. When the version cannot be read
/// (Redis down) the cache is skipped entirely and the numbers come from PostgreSQL (rule 25).
/// </summary>
public sealed class GetDashboardSummaryQueryHandler(
    ICurrentTenant currentTenant,
    ICacheService cache,
    IDashboardQueries queries,
    IClock clock) : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummary>>
{
    public async Task<Result<DashboardSummary>> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentTenant.ShopId is not { } shopId
            || await cache.GetVersionAsync(CacheKeys.CatalogVersion(shopId), cancellationToken) is not { } version)
        {
            return await LoadAsync(cancellationToken);
        }

        var key = CacheKeys.DashboardSummary(shopId, version, clock.Today());
        var cached = await cache.GetAsync<DashboardSummary>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var summary = await LoadAsync(cancellationToken);
        await cache.SetAsync(key, summary, CacheKeys.DashboardSummaryTimeToLive, cancellationToken);
        return summary;
    }

    private async Task<DashboardSummary> LoadAsync(CancellationToken cancellationToken)
    {
        var (dayStart, dayEnd) = clock.CairoDayRangeUtc();
        var expiringUntil = clock.Today().AddDays(DashboardLimits.DefaultExpiringDays);
        var counts = await queries.GetCountsAsync(dayStart, dayEnd, expiringUntil, cancellationToken);
        return new DashboardSummary(
            counts.ProductCount,
            counts.LowStockCount,
            counts.NegativeStockCount,
            counts.ExpiringSoonCount,
            new TodaySales(counts.TodaySalesCount, ProductMapping.Money(counts.TodaySalesTotal), counts.TodayOverriddenSalesCount),
            counts.InvoicesAwaitingReview,
            clock.UtcNow.UtcDateTime);
    }
}
