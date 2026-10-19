using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Billing;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using DomainPayment = Stockly.Domain.Payments.Payment;
using DomainPaymentMethod = Stockly.Domain.Payments.PaymentMethod;
using DomainPaymentProvider = Stockly.Domain.Payments.PaymentProvider;
using DomainPaymentStatus = Stockly.Domain.Payments.PaymentStatus;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>
/// The payment projection shared by the shop's billing reads (tenant-filtered) and the admin's (cross-tenant): one join
/// to the plan (primary key) in the same statement, then the contract DTO with money strings and the plan name in the
/// request language.
/// </summary>
internal static class PaymentProjection
{
    public static IQueryable<PaymentRow> Rows(IQueryable<DomainPayment> payments, IQueryable<Plan> plans) =>
        payments.Join(
            plans,
            payment => payment.PlanId,
            plan => plan.Id,
            (payment, plan) => new PaymentRow
            {
                Id = payment.Id,
                ShopId = payment.ShopId,
                ClientRequestId = payment.ClientRequestId,
                PlanId = payment.PlanId,
                PlanNameAr = plan.NameAr,
                PlanNameEn = plan.NameEn,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Method = payment.Method,
                Status = payment.Status,
                Provider = payment.Provider,
                ProviderTransactionId = payment.ProviderTransactionId,
                CheckoutUrl = payment.CheckoutUrl,
                ReferenceNumber = payment.ReferenceNumber,
                ExpiresAt = payment.ExpiresAt,
                FailureReason = payment.FailureReason,
                CreatedAt = payment.CreatedAt,
                PaidAt = payment.PaidAt,
            });

    public static PaymentDto ToDto(this PaymentRow row, PreferredLanguage language)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new PaymentDto(
            row.Id,
            row.ClientRequestId,
            row.PlanId,
            AdminMapping.PlanName(row.PlanNameAr, row.PlanNameEn, language),
            ProductMapping.Money(row.Amount),
            row.Currency,
            (PaymentMethod)row.Method,
            (PaymentStatus)row.Status,
            row.CheckoutUrl,
            row.ReferenceNumber,
            row.ExpiresAt?.UtcDateTime,
            row.FailureReason,
            row.CreatedAt.UtcDateTime,
            row.PaidAt?.UtcDateTime);
    }

    public static AdminPayment ToAdmin(this PaymentRow row, string shopName, PreferredLanguage language)
    {
        var dto = row.ToDto(language);
        return new AdminPayment(
            dto.Id,
            dto.ClientRequestId,
            dto.PlanId,
            dto.PlanName,
            dto.Amount,
            dto.Currency,
            dto.Method,
            dto.Status,
            dto.CheckoutUrl,
            dto.ReferenceNumber,
            dto.ExpiresAt,
            dto.FailureReason,
            dto.CreatedAt,
            dto.PaidAt,
            row.ShopId,
            shopName,
            (PaymentProvider)row.Provider,
            row.ProviderTransactionId);
    }

    // Member-init projection so EF can still filter and order by its members before materializing.
    internal sealed class PaymentRow
    {
        public Guid Id { get; init; }

        public Guid ShopId { get; init; }

        public Guid ClientRequestId { get; init; }

        public Guid PlanId { get; init; }

        public required string PlanNameAr { get; init; }

        public required string PlanNameEn { get; init; }

        public decimal Amount { get; init; }

        public required string Currency { get; init; }

        public DomainPaymentMethod Method { get; init; }

        public DomainPaymentStatus Status { get; init; }

        public DomainPaymentProvider Provider { get; init; }

        public string? ProviderTransactionId { get; init; }

        public string? CheckoutUrl { get; init; }

        public string? ReferenceNumber { get; init; }

        public DateTimeOffset? ExpiresAt { get; init; }

        public string? FailureReason { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? PaidAt { get; init; }
    }
}
