using MediatR;
using Stockly.Contracts.Billing;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Billing.Checkout;

/// <summary>
/// B3 (owner): starts a payment for one month of <see cref="PlanId"/> with <see cref="Method"/>. Idempotent by
/// <see cref="ClientRequestId"/> (rule 20, ADR-0006): a repeat returns the existing payment. Not wrapped in the
/// transaction behavior: the provider is called before any transaction, and the handler owns the short transaction that
/// stores the payment, so a concurrent repeat that loses the insert is answered with the winner.
/// </summary>
public sealed record CheckoutCommand(Guid ClientRequestId, Guid PlanId, PaymentMethod Method, string? WalletPhone)
    : IRequest<Result<CheckoutResult>>;

/// <summary>The payment and whether this request created it (201) or repeated an earlier one (200).</summary>
public sealed record CheckoutResult(PaymentDto Payment, bool Created);
