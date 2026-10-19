namespace Stockly.Contracts.Billing;

/// <summary>How the owner pays (contract §12.1). Serialized as the PascalCase name.</summary>
public enum PaymentMethod
{
    Card = 1,
    Wallet = 2,
    Kiosk = 3,
}

/// <summary>State of a payment (contract §12.1). Serialized as the PascalCase name.</summary>
public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Expired = 4,
}

/// <summary>The provider that holds a payment (contract §12.4, AD21). Serialized as the PascalCase name.</summary>
public enum PaymentProvider
{
    Paymob = 1,
}

/// <summary>
/// B1: an active plan the shop can see. <see cref="Name"/> is in the request's language; <see cref="MonthlyPrice"/> is an
/// EGP money string with 2 decimals. <see cref="IsCurrent"/> marks the plan of the shop's active subscription.
/// </summary>
public sealed record BillingPlan(
    Guid Id, string Code, string Name, string MonthlyPrice, int DailyScanQuota, int MaxMembers, bool IsFree, bool IsCurrent);

/// <summary>
/// B2: the shop's active subscription. <see cref="EndsOn"/> <c>null</c> is open-ended (free plan).
/// <see cref="PendingPayment"/> is the newest pending payment of the shop, if any.
/// </summary>
public sealed record SubscriptionSummary(
    BillingPlan Plan,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int EffectiveDailyScanQuota,
    int ScansToday,
    int MemberCount,
    PaymentDto? PendingPayment);

/// <summary>
/// One payment (B3–B6). <see cref="CheckoutUrl"/> (card, wallet) is the provider-hosted page to open in the system
/// browser; <see cref="ReferenceNumber"/> (kiosk) is paid in cash at a kiosk partner. After <see cref="ExpiresAt"/> a
/// pending payment becomes expired. <see cref="FailureReason"/> is provider text, informational only.
/// </summary>
public sealed record PaymentDto(
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
    DateTime? PaidAt);

/// <summary>
/// B3 <c>POST /api/billing/checkout</c>. <see cref="ClientRequestId"/> is created once per checkout and reused on every
/// retry (ADR-0006). <see cref="WalletPhone"/> is required for <c>Wallet</c> (<c>^01[0125][0-9]{8}$</c>), ignored otherwise.
/// The amount is never sent: it is the plan's price.
/// </summary>
public sealed record CheckoutRequest(Guid ClientRequestId, Guid PlanId, PaymentMethod Method, string? WalletPhone = null);
