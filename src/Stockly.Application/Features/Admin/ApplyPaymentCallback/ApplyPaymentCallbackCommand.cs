using Stockly.Application.Abstractions.Messaging;
using Stockly.Application.Abstractions.Payments;

namespace Stockly.Application.Features.Admin.ApplyPaymentCallback;

/// <summary>
/// A provider's transaction callback (contract §12.3, ADR-0015 §4). Anonymous and cross-tenant: the payment is found by
/// the provider's signed order id. An invalid signature is <c>Payment.InvalidCallback</c> (401); every verified callback
/// succeeds (200), also for repeats, unknown orders and mismatches, which only change nothing.
/// </summary>
public sealed record ApplyPaymentCallbackCommand(ProviderCallback Callback) : ICommand<PaymentCallbackResult>;

/// <summary>What the callback did; <see cref="PaymentId"/> is <c>null</c> for an unknown order.</summary>
public sealed record PaymentCallbackResult(Guid? PaymentId, SettlementOutcome Outcome);
