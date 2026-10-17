using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>
/// Cross-tenant shop writes of the platform admin (ADR-0005, update 2026-10-18). Loaded rows are tracked and saved with the
/// unit of work in the command's transaction.
/// </summary>
internal sealed class AdminShopRepository(StocklyDbContext db) : IAdminShopRepository
{
    // Primary key. Shops are not tenant entities.
    public Task<Shop?> FindAsync(Guid shopId, CancellationToken cancellationToken) =>
        db.Shops.SingleOrDefaultAsync(shop => shop.Id == shopId, cancellationToken);

    // ux_shop_subscriptions_shop_id_active.
    public Task<ShopSubscription?> FindActiveSubscriptionAsync(Guid shopId, CancellationToken cancellationToken) =>
        db.Set<ShopSubscription>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                subscription => subscription.ShopId == shopId && subscription.Status == SubscriptionStatus.Active, cancellationToken);

    // The tenant stamp refuses a tenant row without a current tenant, and an admin token has none: the admin's only
    // cross-tenant insert is this explicit statement, after the pending changes (the ended subscription) are saved.
    public async Task AddSubscriptionAsync(ShopSubscription subscription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A subscription change must run inside the command's transaction.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO shop_subscriptions (id, shop_id, plan_id, status, starts_on, ends_on, daily_scan_quota_override, created_at)
            VALUES ({subscription.Id}, {subscription.ShopId}, {subscription.PlanId}, {(short)subscription.Status},
                    {subscription.StartsOn}, {subscription.EndsOn}, {subscription.DailyScanQuotaOverride}, {subscription.CreatedAt})
            """,
            cancellationToken);
    }

    // shop_members PK (shop_id, user_id) gives the members; ix_refresh_tokens_user_id_active their active tokens.
    public Task<int> RevokeMemberRefreshTokensAsync(Guid shopId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        var memberIds = db.Set<ShopMember>().Where(member => member.ShopId == shopId).Select(member => member.UserId);
        return db.Set<RefreshToken>()
            .Where(token => token.RevokedAt == null && memberIds.Contains(token.UserId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, revokedAt), cancellationToken);
    }
}
