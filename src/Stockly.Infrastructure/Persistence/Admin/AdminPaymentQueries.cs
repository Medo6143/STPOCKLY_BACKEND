using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Persistence.Queries;
using ContractPaymentMethod = Stockly.Contracts.Billing.PaymentMethod;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>Cross-tenant payment reads of the platform admin (AD21, AD22; ADR-0005 update 2026-10-18). Indexes in docs/erd.md §3.8.</summary>
internal sealed class AdminPaymentQueries(StocklyDbContext db) : IAdminPaymentQueries
{
    // Every shop: ix_payments_created_at, or ix_payments_status_created_at with a status. One shop:
    // ix_payments_shop_id_created_at. Per row: the plan and the shop by primary key.
    public async Task<PagedResponse<AdminPayment>> ListAsync(
        Guid? shopId, PaymentStatus? status, int page, int pageSize, PreferredLanguage language, CancellationToken cancellationToken)
    {
        var payments = db.Set<Payment>().IgnoreQueryFilters().AsNoTracking();
        if (shopId is { } shop)
        {
            payments = payments.Where(payment => payment.ShopId == shop);
        }

        if (status is { } wanted)
        {
            payments = payments.Where(payment => payment.Status == wanted);
        }

        var totalCount = await payments.CountAsync(cancellationToken);
        var rows = await PaymentProjection.Rows(
                payments.OrderByDescending(payment => payment.CreatedAt).ThenByDescending(payment => payment.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize),
                db.Set<Plan>().AsNoTracking())
            .Join(db.Shops.AsNoTracking(), row => row.ShopId, candidate => candidate.Id, (row, candidate) => new { row, candidate.Name })
            .OrderByDescending(item => item.row.CreatedAt)
            .ThenByDescending(item => item.row.Id)
            .ToListAsync(cancellationToken);

        return new PagedResponse<AdminPayment>(
            [.. rows.Select(item => item.row.ToAdmin(item.Name, language))], page, pageSize, totalCount);
    }

    // ix_payments_paid_at_paid (paid_at) WHERE status = Paid, grouped by method in PostgreSQL.
    public async Task<PaymentSummary> SummaryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var byMethod = await db.Set<Payment>().IgnoreQueryFilters().AsNoTracking()
            .Where(payment => payment.Status == PaymentStatus.Paid && payment.PaidAt >= fromUtc && payment.PaidAt < toUtc)
            .GroupBy(payment => payment.Method)
            .Select(group => new { Method = group.Key, Count = group.Count(), Amount = group.Sum(payment => payment.Amount) })
            .ToListAsync(cancellationToken);

        var ordered = byMethod.OrderBy(row => row.Method).ToList();
        return new PaymentSummary(
            ordered.Sum(row => row.Count),
            ProductMapping.Money(ordered.Sum(row => row.Amount)),
            [.. ordered.Select(row => new PaymentMethodSummary((ContractPaymentMethod)row.Method, row.Count, ProductMapping.Money(row.Amount)))]);
    }
}
