using Stockly.Contracts.Billing;

namespace Stockly.Contracts.Admin;

/// <summary>
/// AD21: a payment of any shop, the <see cref="PaymentDto"/> fields plus the shop, the provider and the provider's
/// transaction id (newest first).
/// </summary>
public sealed record AdminPayment(
    Guid Id,
    Guid ClientRequestId,
    Guid PlanId,
    string PlanName,
    string Amount,
    string Currency,
    PaymentMethod Method,
    PaymentStatus Status,
    string? CheckoutUrl,
    string? ReferenceNumber,
    DateTime? ExpiresAt,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? PaidAt,
    Guid ShopId,
    string ShopName,
    PaymentProvider Provider,
    string? ProviderTransactionId);

/// <summary>AD22: paid payments of the Africa/Cairo date range, in total and per method (EGP money strings).</summary>
public sealed record PaymentSummary(int PaidCount, string PaidAmount, IReadOnlyList<PaymentMethodSummary> ByMethod);

/// <summary>AD22: paid payments of one method.</summary>
public sealed record PaymentMethodSummary(PaymentMethod Method, int PaidCount, string PaidAmount);
