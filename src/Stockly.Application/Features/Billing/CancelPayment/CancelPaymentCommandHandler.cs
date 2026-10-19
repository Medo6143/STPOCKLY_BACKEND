using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Billing;
using Stockly.Domain.Common;
using Stockly.Domain.Payments;

namespace Stockly.Application.Features.Billing.CancelPayment;

/// <summary>
/// In one transaction: lock the payment (the same row lock a concurrent callback takes first), cancel it, save, and read
/// it back. Not pending is <c>Billing.NotCancelable</c>; another shop's payment is <c>Payment.NotFound</c>.
/// </summary>
public sealed class CancelPaymentCommandHandler(IPaymentRepository payments, IBillingQueries billing, IUnitOfWork unitOfWork)
    : IRequestHandler<CancelPaymentCommand, Result<PaymentDto>>
{
    public async Task<Result<PaymentDto>> Handle(CancelPaymentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payment = await payments.FindForUpdateAsync(request.PaymentId, cancellationToken);
        if (payment is null)
        {
            return PaymentErrors.NotFound;
        }

        var cancelled = payment.Cancel();
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await billing.FindPaymentAsync(payment.Id, AdminMapping.RequestLanguage(), cancellationToken)
            ?? throw new InvalidOperationException("The payment just cancelled was not found.");
    }
}
