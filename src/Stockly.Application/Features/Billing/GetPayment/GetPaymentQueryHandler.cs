using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Billing;
using Stockly.Domain.Common;
using Stockly.Domain.Payments;

namespace Stockly.Application.Features.Billing.GetPayment;

public sealed class GetPaymentQueryHandler(IBillingQueries billing) : IRequestHandler<GetPaymentQuery, Result<PaymentDto>>
{
    public async Task<Result<PaymentDto>> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payment = await billing.FindPaymentAsync(request.PaymentId, AdminMapping.RequestLanguage(), cancellationToken);
        if (payment is null)
        {
            return PaymentErrors.NotFound;
        }

        return payment;
    }
}
