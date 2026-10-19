using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Billing;

namespace Stockly.Application.Features.Billing.GetPayment;

/// <summary>B4: one payment of the shop. Another shop's payment is <c>Payment.NotFound</c> (AGENTS.md rule 10).</summary>
public sealed record GetPaymentQuery(Guid PaymentId) : IQuery<PaymentDto>;
