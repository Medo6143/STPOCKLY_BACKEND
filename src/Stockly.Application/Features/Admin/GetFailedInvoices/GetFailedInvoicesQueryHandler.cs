using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.GetFailedInvoices;

public sealed class GetFailedInvoicesQueryHandler(IAdminInvoiceQueries invoices)
    : IRequestHandler<GetFailedInvoicesQuery, Result<PagedResponse<AdminFailedInvoice>>>
{
    public async Task<Result<PagedResponse<AdminFailedInvoice>>> Handle(GetFailedInvoicesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await invoices.ListFailedAsync(request.ShopId, request.Page, request.PageSize, cancellationToken);
    }
}
