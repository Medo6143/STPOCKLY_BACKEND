using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Billing.GetPayments;

public sealed class GetPaymentsQueryHandler(IBillingQueries billing)
    : IRequestHandler<GetPaymentsQuery, Result<PagedResponse<PaymentDto>>>
{
    public async Task<Result<PagedResponse<PaymentDto>>> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await billing.ListPaymentsAsync(request.Page, request.PageSize, AdminMapping.RequestLanguage(), cancellationToken);
    }
}
