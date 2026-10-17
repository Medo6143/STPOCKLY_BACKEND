using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Queries;

internal sealed class ShopPlanQueries(StocklyDbContext db) : IShopPlanQueries
{
    // ux_shop_subscriptions_shop_id_active (shop_id) WHERE status = Active, then the plan by primary key.
    public Task<int?> GetDailyScanQuotaAsync(CancellationToken cancellationToken) =>
        db.Set<ShopSubscription>().AsNoTracking()
            .Where(subscription => subscription.Status == SubscriptionStatus.Active)
            .Join(db.Set<Plan>(), subscription => subscription.PlanId, plan => plan.Id, (_, plan) => (int?)plan.DailyScanQuota)
            .SingleOrDefaultAsync(cancellationToken);
}
