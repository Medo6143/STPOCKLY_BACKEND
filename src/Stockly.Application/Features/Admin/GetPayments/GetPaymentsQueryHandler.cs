using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;
using DomainPaymentStatus = Stockly.Domain.Payments.PaymentStatus;

namespace Stockly.Application.Features.Admin.GetPayments;

public sealed class GetPaymentsQueryHandler(IAdminPaymentQueries payments)
    : IRequestHandler<GetPaymentsQuery, Result<PagedResponse<AdminPayment>>>
{
    public async Task<Result<PagedResponse<AdminPayment>>> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DomainPaymentStatus? status = request.Status is { } value ? (DomainPaymentStatus)value : null;
        return await payments.ListAsync(
            request.ShopId, status, request.Page, request.PageSize, AdminMapping.RequestLanguage(), cancellationToken);
    }
}
