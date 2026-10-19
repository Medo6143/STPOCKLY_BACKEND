using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Persistence.Configurations;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>
/// Cross-tenant payment and subscription writes of the callback, the reconcile job and the downgrade job (ADR-0015 §4,
/// §5; ADR-0005 update 2026-10-18). Locks come first (<c>FOR UPDATE</c>), then the read sees the latest row; the payment
/// is always locked before the subscription (AGENTS.md rule 4). Indexes in docs/erd.md §3.1 and §3.8.
/// </summary>
internal sealed class AdminPaymentRepository(StocklyDbContext db) : IAdminPaymentRepository
{
    private const string LockByOrderSql =
        "SELECT 1 FROM " + PaymentConfiguration.TableName + " WHERE provider = {0} AND provider_order_id = {1} FOR UPDATE";

    private const string LockByIdSql = "SELECT 1 FROM " + PaymentConfiguration.TableName + " WHERE id = {0} FOR UPDATE";

    private static readonly string LockSubscriptionSql =
        "SELECT 1 FROM " + ShopSubscriptionConfiguration.TableName
        + " WHERE shop_id = {0} AND status = " + (short)SubscriptionStatus.Active + " FOR UPDATE";

    // ux_payments_provider_provider_order_id.
    public async Task<Payment?> FindByProviderOrderForUpdateAsync(
        PaymentProvider provider, string providerOrderId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlRawAsync(LockByOrderSql, [(short)provider, providerOrderId], cancellationToken);
        return await db.Set<Payment>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(payment => payment.Provider == provider && payment.ProviderOrderId == providerOrderId, cancellationToken);
    }

    // Primary key.
    public async Task<Payment?> FindForUpdateAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlRawAsync(LockByIdSql, [paymentId], cancellationToken);
        return await db.Set<Payment>().IgnoreQueryFilters().SingleOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken);
    }

    // ix_payments_expires_at_pending (expires_at) WHERE status = Pending.
    public async Task<IReadOnlyList<ExpiredPayment>> ListExpiredPendingAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await db.Set<Payment>().IgnoreQueryFilters().AsNoTracking()
            .Where(payment => payment.Status == PaymentStatus.Pending && payment.ExpiresAt <= now)
            .OrderBy(payment => payment.ExpiresAt)
            .ThenBy(payment => payment.Id)
            .Take(limit)
            .Select(payment => new ExpiredPayment(payment.Id, payment.ShopId, payment.Provider, payment.ProviderOrderId))
            .ToListAsync(cancellationToken);

    // ux_shop_subscriptions_shop_id_active. The read after the lock sees a subscription a concurrent commit started.
    public async Task<ShopSubscription?> FindActiveSubscriptionForUpdateAsync(Guid shopId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlRawAsync(LockSubscriptionSql, [shopId], cancellationToken);
        return await db.Set<ShopSubscription>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                subscription => subscription.ShopId == shopId && subscription.Status == SubscriptionStatus.Active, cancellationToken);
    }

    // ix_shop_subscriptions_ends_on_active (ends_on) WHERE status = Active.
    public async Task<IReadOnlyList<Guid>> ListShopsWithLapsedSubscriptionAsync(
        DateOnly today, Guid defaultPlanId, int limit, CancellationToken cancellationToken) =>
        await db.Set<ShopSubscription>().IgnoreQueryFilters().AsNoTracking()
            .Where(subscription => subscription.Status == SubscriptionStatus.Active
                && subscription.EndsOn < today
                && subscription.PlanId != defaultPlanId)
            .OrderBy(subscription => subscription.EndsOn)
            .ThenBy(subscription => subscription.ShopId)
            .Take(limit)
            .Select(subscription => subscription.ShopId)
            .ToListAsync(cancellationToken);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A payment lock must be taken inside a transaction, or it is released at once.");
        }
    }
}
