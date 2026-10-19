using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Payments;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.ApplyPaymentCallback;

/// <summary>
/// Verify the signature (the gateway), lock the payment of the signed order id, then let <see cref="PaymentSettlement"/>
/// apply the transaction, in the command's transaction. Two identical callbacks serialize on the payment lock: the second
/// finds it Paid and changes nothing, so the subscription is extended once.
/// </summary>
public sealed class ApplyPaymentCallbackCommandHandler(
    IPaymentGateway gateway,
    IAdminPaymentRepository payments,
    PaymentSettlement settlement) : IRequestHandler<ApplyPaymentCallbackCommand, Result<PaymentCallbackResult>>
{
    public async Task<Result<PaymentCallbackResult>> Handle(ApplyPaymentCallbackCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transaction = gateway.ReadCallback(request.Callback);
        if (transaction.IsFailure)
        {
            return transaction.Error;
        }

        var payment = await payments.FindByProviderOrderForUpdateAsync(
            gateway.Provider, transaction.Value.ProviderOrderId, cancellationToken);
        if (payment is null)
        {
            return new PaymentCallbackResult(null, SettlementOutcome.UnknownOrder);
        }

        var outcome = await settlement.ApplyAsync(payment, transaction.Value, cancellationToken);
        return new PaymentCallbackResult(payment.Id, outcome);
    }
}
