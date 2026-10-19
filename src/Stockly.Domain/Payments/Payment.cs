using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Payments;

/// <summary>
/// One checkout of one month of a plan (ADR-0015, contract §12). Created <see cref="PaymentStatus.Pending"/> only after
/// the provider accepted the checkout, so a provider failure leaves nothing behind. Only a verified provider callback or
/// the reconcile job makes it <see cref="PaymentStatus.Paid"/> (ADR-0015 §4); the owner may cancel a pending one (B6).
/// The amount is the plan's price at checkout, never a client value (ADR-0015 §6).
/// </summary>
public sealed class Payment : Entity, ITenantEntity
{
    /// <summary>Stockly charges in Egyptian pounds only.</summary>
    public const string Egp = "EGP";

    public const int ProviderIdMaxLength = 100;
    public const int CheckoutUrlMaxLength = 2048;
    public const int ReferenceNumberMaxLength = 100;
    public const int FailureReasonMaxLength = 500;

    private Payment(
        Guid shopId,
        Guid planId,
        Guid clientRequestId,
        decimal amount,
        PaymentMethod method,
        PaymentProvider provider,
        DateTimeOffset createdAt)
    {
        ShopId = shopId;
        PlanId = planId;
        ClientRequestId = clientRequestId;
        Amount = amount;
        Currency = Egp;
        Method = method;
        Provider = provider;
        Status = PaymentStatus.Pending;
        ProviderOrderId = string.Empty;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    /// <summary>The plan this payment buys one month of.</summary>
    public Guid PlanId { get; }

    /// <summary>Client-generated id of the checkout; unique per shop (ADR-0006, contract §12 B3).</summary>
    public Guid ClientRequestId { get; }

    /// <summary>EGP, the plan's monthly price at checkout time.</summary>
    public decimal Amount { get; }

    public string Currency { get; }

    public PaymentMethod Method { get; }

    public PaymentProvider Provider { get; }

    public PaymentStatus Status { get; private set; }

    /// <summary>The provider's order id; its callbacks are matched by it. Set once by <see cref="AttachCheckout"/>.</summary>
    public string ProviderOrderId { get; private set; }

    /// <summary>The provider transaction that last changed the status; unique (callback idempotency).</summary>
    public string? ProviderTransactionId { get; private set; }

    /// <summary>Card and wallet: the provider-hosted page.</summary>
    public string? CheckoutUrl { get; private set; }

    /// <summary>Kiosk: the reference paid in cash.</summary>
    public string? ReferenceNumber { get; private set; }

    /// <summary>After this a pending payment is no longer open; the reconcile job settles it.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Provider text of the last failure, informational only.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>
    /// A pending payment for one month of <paramref name="planId"/>. Its <see cref="Entity.Id"/> is the reference sent to
    /// the provider; it is stored only after <see cref="AttachCheckout"/>.
    /// </summary>
    public static Result<Payment> Start(
        Guid shopId,
        Guid planId,
        Guid clientRequestId,
        decimal amount,
        PaymentMethod method,
        PaymentProvider provider,
        DateTimeOffset now)
    {
        var error = (shopId == Guid.Empty ? PaymentErrors.ShopRequired : null)
            ?? (planId == Guid.Empty ? PaymentErrors.PlanRequired : null)
            ?? (clientRequestId == Guid.Empty ? PaymentErrors.ClientRequestIdRequired : null)
            ?? (!Enum.IsDefined(method) || !Enum.IsDefined(provider) ? PaymentErrors.UnknownMethod : null)
            ?? (amount <= 0m ? PaymentErrors.FreePlan : null)
            ?? Precision.ValidateMoney(amount, nameof(Amount));
        if (error is not null)
        {
            return error;
        }

        return new Payment(shopId, planId, clientRequestId, amount, method, provider, now);
    }

    /// <summary>
    /// Records the provider's checkout: its order id, the hosted page or the kiosk reference, and the expiry. Once only:
    /// the order id never changes afterwards (callbacks are matched by it).
    /// </summary>
    public Result AttachCheckout(string providerOrderId, string? checkoutUrl, string? referenceNumber, DateTimeOffset expiresAt)
    {
        if (ProviderOrderId.Length > 0)
        {
            return PaymentErrors.CheckoutAttached;
        }

        var orderId = OptionalText.Clean(providerOrderId);
        if (orderId is null || orderId.Length > ProviderIdMaxLength)
        {
            return PaymentErrors.ProviderOrderRequired;
        }

        ProviderOrderId = orderId;
        CheckoutUrl = Limit(checkoutUrl, CheckoutUrlMaxLength);
        ReferenceNumber = Limit(referenceNumber, ReferenceNumberMaxLength);
        ExpiresAt = expiresAt;
        return Result.Success();
    }

    /// <summary>Pending and not past its expiry: it blocks a new checkout (B3 <c>Billing.PaymentPending</c>).</summary>
    public bool IsOpenAt(DateTimeOffset now) => Status == PaymentStatus.Pending && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Whether a provider transaction is for this payment: same order, same amount, same currency (ADR-0015 §4).</summary>
    public bool Matches(string providerOrderId, decimal amount, string currency) =>
        string.Equals(ProviderOrderId, providerOrderId, StringComparison.Ordinal)
        && Amount == amount
        && string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The provider took the money. Any state but <see cref="PaymentStatus.Paid"/> becomes Paid (a cancelled or failed
    /// payment included: the money was taken). Returns <c>false</c> when it was already Paid (Paid is final, nothing changes).
    /// </summary>
    public Result<bool> MarkPaid(string providerTransactionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(providerTransactionId))
        {
            return PaymentErrors.TransactionRequired;
        }

        if (Status == PaymentStatus.Paid)
        {
            return false;
        }

        Status = PaymentStatus.Paid;
        ProviderTransactionId = Limit(providerTransactionId, ProviderIdMaxLength);
        PaidAt = now;
        FailureReason = null;
        return true;
    }

    /// <summary>The provider declined the payment. Only a pending payment fails; returns whether it changed.</summary>
    public Result<bool> MarkFailed(string providerTransactionId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(providerTransactionId))
        {
            return PaymentErrors.TransactionRequired;
        }

        if (Status != PaymentStatus.Pending)
        {
            return false;
        }

        Status = PaymentStatus.Failed;
        ProviderTransactionId = Limit(providerTransactionId, ProviderIdMaxLength);
        FailureReason = Limit(reason, FailureReasonMaxLength);
        return true;
    }

    /// <summary>B6: the owner gives up a pending payment (status <see cref="PaymentStatus.Expired"/>).</summary>
    public Result Cancel()
    {
        if (Status != PaymentStatus.Pending)
        {
            return PaymentErrors.NotCancelable;
        }

        Status = PaymentStatus.Expired;
        return Result.Success();
    }

    /// <summary>The reconcile job found no payment at the provider. Only a pending payment expires; returns whether it changed.</summary>
    public bool Expire()
    {
        if (Status != PaymentStatus.Pending)
        {
            return false;
        }

        Status = PaymentStatus.Expired;
        return true;
    }

    private static string? Limit(string? value, int maxLength)
    {
        var clean = OptionalText.Clean(value);
        return clean is null || clean.Length <= maxLength ? clean : clean[..maxLength];
    }
}
