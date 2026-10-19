using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Persistence.Configurations;

namespace Stockly.Infrastructure.Persistence.Repositories;

/// <summary>Payment writes and decisions of the current shop. Tenant-filtered (ADR-0002); indexes in docs/erd.md §3.8.</summary>
internal sealed class PaymentRepository(StocklyDbContext db, ICurrentTenant currentTenant) : IPaymentRepository
{
    private const string LockPaymentSql =
        "SELECT 1 FROM " + PaymentConfiguration.TableName + " WHERE id = {0} AND shop_id = {1} FOR UPDATE";

    private static readonly string LockSubscriptionSql =
        "SELECT 1 FROM " + ShopSubscriptionConfiguration.TableName
        + " WHERE shop_id = {0} AND status = " + (short)SubscriptionStatus.Active + " FOR UPDATE";

    public void Add(Payment payment) => db.Set<Payment>().Add(payment);

    // Primary key; the lock first, so the read sees the latest row.
    public async Task<Payment?> FindForUpdateAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (currentTenant.ShopId is not { } shopId)
        {
            return null;
        }

        await db.Database.ExecuteSqlRawAsync(LockPaymentSql, [paymentId, shopId], cancellationToken);
        return await db.Set<Payment>().SingleOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken);
    }

    // ux_shop_subscriptions_shop_id_active.
    public async Task LockCheckoutsAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (currentTenant.ShopId is { } shopId)
        {
            await db.Database.ExecuteSqlRawAsync(LockSubscriptionSql, [shopId], cancellationToken);
        }
    }

    // ix_payments_shop_id_created_at_pending (shop_id, created_at DESC) WHERE status = Pending.
    public Task<bool> HasOpenPaymentAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<Payment>().AsNoTracking()
            .AnyAsync(
                payment => payment.Status == PaymentStatus.Pending && (payment.ExpiresAt == null || payment.ExpiresAt > now),
                cancellationToken);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A payment lock must be taken inside a transaction, or it is released at once.");
        }
    }
}
