using Stockly.Domain.Common;
using Stockly.Domain.Payments;

namespace Stockly.Application.Abstractions.Payments;

/// <summary>
/// The payment provider port (ADR-0015 §1). Infrastructure implements it per provider (Paymob in
/// <c>Infrastructure/Payments/Paymob</c>); a new provider is a new class (SOLID O). Implementations never log secrets,
/// callback bodies, signatures or full phone numbers (AGENTS.md rule 12).
/// </summary>
public interface IPaymentGateway
{
    PaymentProvider Provider { get; }

    /// <summary>Whether <paramref name="method"/> is configured. Every method is unavailable while payments are disabled.</summary>
    bool IsAvailable(PaymentMethod method);

    /// <summary>
    /// Creates the provider's checkout for a pending payment (hosted page for card and wallet, reference number for kiosk).
    /// A provider error (4xx, 5xx, timeout, unreadable answer) is <c>Billing.ProviderUnavailable</c>: nothing was charged.
    /// </summary>
    Task<Result<ProviderCheckout>> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads and verifies a provider callback (ADR-0015 §4). An invalid or missing signature, or a body that is not a
    /// transaction callback, is <c>Payment.InvalidCallback</c> (401). The transaction is not trusted beyond its signature:
    /// the caller still compares order, amount and currency with the stored payment.
    /// </summary>
    Result<ProviderTransaction> ReadCallback(ProviderCallback callback);

    /// <summary>
    /// The provider's latest transaction of an order (the reconcile job); <c>null</c> when the provider has none. A provider
    /// error is <c>Billing.ProviderUnavailable</c>, and the payment is left as it is.
    /// </summary>
    Task<Result<ProviderTransaction?>> FindLatestTransactionAsync(string providerOrderId, CancellationToken cancellationToken);
}

/// <summary>
/// What the provider needs for a checkout. <see cref="PaymentId"/> is our reference (<c>special_reference</c>);
/// <see cref="Amount"/> is EGP with 2 decimals; the payer is the shop owner.
/// </summary>
public sealed record ProviderCheckoutRequest(
    Guid PaymentId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string PlanName,
    Payer Payer,
    string? WalletPhone);

/// <summary>The shop owner, as the provider's billing data.</summary>
public sealed record Payer(string FullName, string Email, string? Phone);

/// <summary>The provider's answer to a checkout.</summary>
public sealed record ProviderCheckout(string ProviderOrderId, string? CheckoutUrl, string? ReferenceNumber, DateTimeOffset ExpiresAt);

/// <summary>A callback as received: the raw body and the query string (the signature may travel in either).</summary>
public sealed record ProviderCallback(string Body, IReadOnlyDictionary<string, string> Query);

/// <summary>How a provider transaction ended.</summary>
public enum ProviderTransactionOutcome
{
    /// <summary>The money was taken.</summary>
    Succeeded = 1,

    /// <summary>Still waiting (e.g. a kiosk reference not paid yet).</summary>
    Pending = 2,

    /// <summary>Declined, voided or refunded.</summary>
    Failed = 3,
}

/// <summary>A verified provider transaction. <see cref="Amount"/> is EGP converted from the provider's minor units.</summary>
public sealed record ProviderTransaction(
    string TransactionId,
    string ProviderOrderId,
    decimal Amount,
    string Currency,
    ProviderTransactionOutcome Outcome,
    string? FailureReason);
