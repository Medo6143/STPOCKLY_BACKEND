using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Queries;

internal sealed class ShopPlanQueries(StocklyDbContext db) : IShopPlanQueries
{
    // ux_shop_subscriptions_shop_id_active (shop_id) WHERE status = Active, then the plan by primary key. The override
    // wins over the plan (contract AD9, effectiveDailyScanQuota); never cached, so a change applies to the next scan.
    public Task<int?> GetDailyScanQuotaAsync(CancellationToken cancellationToken) =>
        db.Set<ShopSubscription>().AsNoTracking()
            .Where(subscription => subscription.Status == SubscriptionStatus.Active)
            .Join(
                db.Set<Plan>(),
                subscription => subscription.PlanId,
                plan => plan.Id,
                (subscription, plan) => (int?)(subscription.DailyScanQuotaOverride ?? plan.DailyScanQuota))
            .SingleOrDefaultAsync(cancellationToken);
}
