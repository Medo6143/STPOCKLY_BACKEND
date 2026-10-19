using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;
using Stockly.Domain.Users;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>
/// Read-only billing projections of the current shop (tenant-filtered, no tracking, AGENTS.md rule 33). Plan names are
/// in <c>language</c>.
/// </summary>
public interface IBillingQueries
{
    /// <summary>B1: active plans, free first, then by price, then by code; the shop's current plan marked.</summary>
    Task<IReadOnlyList<BillingPlan>> ListActivePlansAsync(PreferredLanguage language, CancellationToken cancellationToken);

    /// <summary>B2: the active subscription with its plan (active or not); <c>null</c> when the shop has none.</summary>
    Task<ActiveSubscription?> FindActiveSubscriptionAsync(PreferredLanguage language, CancellationToken cancellationToken);

    /// <summary>B4: the payment; <c>null</c> when missing or another shop's.</summary>
    Task<PaymentDto?> FindPaymentAsync(Guid paymentId, PreferredLanguage language, CancellationToken cancellationToken);

    /// <summary>B3 repeat: the payment created with this client request id (ADR-0006); <c>null</c> when none.</summary>
    Task<PaymentDto?> FindPaymentByClientRequestIdAsync(Guid clientRequestId, PreferredLanguage language, CancellationToken cancellationToken);

    /// <summary>B2: the shop's newest pending payment; <c>null</c> when none.</summary>
    Task<PaymentDto?> FindNewestPendingPaymentAsync(PreferredLanguage language, CancellationToken cancellationToken);

    /// <summary>B5: a page of the shop's payments, newest first.</summary>
    Task<PagedResponse<PaymentDto>> ListPaymentsAsync(int page, int pageSize, PreferredLanguage language, CancellationToken cancellationToken);
}

/// <summary>The shop's active subscription with its plan (B2).</summary>
public sealed record ActiveSubscription(BillingPlan Plan, DateOnly StartsOn, DateOnly? EndsOn, int EffectiveDailyScanQuota);
