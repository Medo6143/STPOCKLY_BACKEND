using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Billing;

namespace Stockly.Application.Features.Billing.CancelPayment;

/// <summary>
/// B6 (owner): gives up a pending payment so the owner can start another method; it becomes <c>Expired</c>. A late
/// provider success for it is still honoured by the callback (the money was taken).
/// </summary>
public sealed record CancelPaymentCommand(Guid PaymentId) : ICommand<PaymentDto>;
