using Stockly.Domain.Common;

namespace Stockly.Domain.Payments;

/// <summary>Payment and billing error codes (contract §9, §12).</summary>
public static class PaymentErrors
{
    /// <summary>No such payment in the current shop. Also returned for another shop's payment (AGENTS.md rule 10).</summary>
    public static readonly Error NotFound =
        Error.NotFound("Payment.NotFound", "The payment was not found.");

    /// <summary>B6: only a pending payment can be cancelled.</summary>
    public static readonly Error NotCancelable =
        Error.Conflict("Billing.NotCancelable", "Only a pending payment can be cancelled.");

    /// <summary>B3: a free plan (price 0) cannot be checked out.</summary>
    public static readonly Error FreePlan =
        Error.Validation("Billing.FreePlan", "A free plan cannot be checked out.");

    /// <summary>B3: the payment method is not configured (or payments are disabled).</summary>
    public static readonly Error MethodUnavailable =
        Error.Validation("Billing.MethodUnavailable", "This payment method is not available.");

    /// <summary>B3: the shop already has a pending payment that has not expired.</summary>
    public static readonly Error PaymentPending =
        Error.Conflict("Billing.PaymentPending", "The shop already has a pending payment.");

    /// <summary>B3: the provider failed; nothing was charged and nothing was stored. Retry with the same client request id.</summary>
    public static readonly Error ProviderUnavailable =
        Error.BadGateway("Billing.ProviderUnavailable", "The payment provider is unavailable. Nothing was charged.");

    /// <summary>
    /// A provider callback whose signature does not verify (contract §12.3): answered <c>401</c> with no body detail. An
    /// operations code, not in the endpoint catalogue.
    /// </summary>
    public static readonly Error InvalidCallback =
        Error.Unauthorized("Payment.InvalidCallback", "The callback signature is invalid.");

    /// <summary>Internal, never returned: a concurrent first request with the same <c>clientRequestId</c> won the insert.</summary>
    public static readonly Error DuplicateClientRequest =
        Error.Conflict("Payment.DuplicateClientRequest", "A payment with this client request id already exists.");

    /// <summary>Internal guard: a payment belongs to a shop.</summary>
    public static readonly Error ShopRequired =
        Error.Validation("Payment.ShopRequired", "A payment must belong to a shop.");

    /// <summary>Internal guard: a payment buys a plan.</summary>
    public static readonly Error PlanRequired =
        Error.Validation("Payment.PlanRequired", "A payment must reference a plan.");

    /// <summary>Internal guard (the request validator rejects it first).</summary>
    public static readonly Error ClientRequestIdRequired =
        Error.Validation("Payment.ClientRequestIdRequired", "A payment needs a client request id.");

    /// <summary>Internal guard: the provider's order id is what its callbacks are matched by.</summary>
    public static readonly Error ProviderOrderRequired =
        Error.Validation("Payment.ProviderOrderRequired", "A payment needs the provider's order id.");

    /// <summary>Internal guard: the provider's checkout is attached once.</summary>
    public static readonly Error CheckoutAttached =
        Error.Conflict("Payment.CheckoutAttached", "The payment already has a provider checkout.");

    /// <summary>Internal guard (the request validator rejects it first).</summary>
    public static readonly Error UnknownMethod =
        Error.Validation("Payment.UnknownMethod", "The payment method is not supported.");

    /// <summary>Internal guard: a provider transaction needs its id (callback idempotency).</summary>
    public static readonly Error TransactionRequired =
        Error.Validation("Payment.TransactionRequired", "A provider transaction id is required.");
}
