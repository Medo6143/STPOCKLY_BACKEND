using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using DomainPaymentStatus = Stockly.Domain.Payments.PaymentStatus;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>
/// Billing reads of the current shop: projections, no tracking, tenant-filtered (payments, subscriptions); plans are
/// global. Indexes in docs/erd.md §3.1 and §3.8.
/// </summary>
internal sealed class BillingQueries(StocklyDbContext db) : IBillingQueries
{
    // Plans: a small global table, read whole. The current plan: ux_shop_subscriptions_shop_id_active.
    public async Task<IReadOnlyList<BillingPlan>> ListActivePlansAsync(PreferredLanguage language, CancellationToken cancellationToken)
    {
        var currentPlanId = await ActiveSubscriptions().Select(subscription => (Guid?)subscription.PlanId).SingleOrDefaultAsync(cancellationToken);
        var plans = await db.Set<Plan>().AsNoTracking()
            .Where(plan => plan.IsActive)
            .OrderBy(plan => plan.MonthlyPrice)
            .ThenBy(plan => plan.Code)
            .ToListAsync(cancellationToken);

        return [.. plans.Select(plan => ToBillingPlan(plan, currentPlanId, language))];
    }

    // ux_shop_subscriptions_shop_id_active, then the plan by primary key.
    public async Task<ActiveSubscription?> FindActiveSubscriptionAsync(PreferredLanguage language, CancellationToken cancellationToken)
    {
        var row = await ActiveSubscriptions()
            .Join(db.Set<Plan>().AsNoTracking(), subscription => subscription.PlanId, plan => plan.Id, (subscription, plan) => new { subscription, plan })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        return new ActiveSubscription(
            ToBillingPlan(row.plan, row.plan.Id, language),
            row.subscription.StartsOn,
            row.subscription.EndsOn,
            row.subscription.EffectiveDailyScanQuota(row.plan.DailyScanQuota));
    }

    // Primary key.
    public Task<PaymentDto?> FindPaymentAsync(Guid paymentId, PreferredLanguage language, CancellationToken cancellationToken) =>
        FirstAsync(Payments().Where(payment => payment.Id == paymentId), language, cancellationToken);

    // ux_payments_shop_id_client_request_id.
    public Task<PaymentDto?> FindPaymentByClientRequestIdAsync(
        Guid clientRequestId, PreferredLanguage language, CancellationToken cancellationToken) =>
        FirstAsync(Payments().Where(payment => payment.ClientRequestId == clientRequestId), language, cancellationToken);

    // ix_payments_shop_id_created_at_pending (shop_id, created_at DESC) WHERE status = Pending.
    public Task<PaymentDto?> FindNewestPendingPaymentAsync(PreferredLanguage language, CancellationToken cancellationToken) =>
        FirstAsync(
            Payments().Where(payment => payment.Status == DomainPaymentStatus.Pending)
                .OrderByDescending(payment => payment.CreatedAt)
                .ThenByDescending(payment => payment.Id),
            language,
            cancellationToken);

    // ix_payments_shop_id_created_at (shop_id, created_at DESC). Newest first; id only breaks ties (ADR-0010).
    public async Task<PagedResponse<PaymentDto>> ListPaymentsAsync(
        int page, int pageSize, PreferredLanguage language, CancellationToken cancellationToken)
    {
        var totalCount = await Payments().CountAsync(cancellationToken);
        var rows = await PaymentProjection.Rows(
                Payments().OrderByDescending(payment => payment.CreatedAt).ThenByDescending(payment => payment.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize),
                db.Set<Plan>().AsNoTracking())
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);

        return new PagedResponse<PaymentDto>([.. rows.Select(row => row.ToDto(language))], page, pageSize, totalCount);
    }

    private static BillingPlan ToBillingPlan(Plan plan, Guid? currentPlanId, PreferredLanguage language) =>
        new(
            plan.Id,
            plan.Code,
            AdminMapping.PlanName(plan.NameAr, plan.NameEn, language),
            ProductMapping.Money(plan.MonthlyPrice),
            plan.DailyScanQuota,
            plan.MaxMembers,
            plan.MonthlyPrice == 0m,
            plan.Id == currentPlanId);

    private IQueryable<ShopSubscription> ActiveSubscriptions() =>
        db.Set<ShopSubscription>().AsNoTracking().Where(subscription => subscription.Status == SubscriptionStatus.Active);

    private IQueryable<Payment> Payments() => db.Set<Payment>().AsNoTracking();

    private async Task<PaymentDto?> FirstAsync(IQueryable<Payment> payments, PreferredLanguage language, CancellationToken cancellationToken)
    {
        var row = await PaymentProjection.Rows(payments.Take(1), db.Set<Plan>().AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
        return row?.ToDto(language);
    }
}
