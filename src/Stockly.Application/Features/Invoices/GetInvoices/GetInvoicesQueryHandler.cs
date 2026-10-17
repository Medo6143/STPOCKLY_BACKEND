using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Application.Features.Invoices.GetInvoices;

/// <summary>I2 (docs/design/backend.md §9.2): paged by <c>created_at DESC</c>, tenant-filtered projection.</summary>
public sealed class GetInvoicesQueryHandler(IInvoiceQueries queries)
    : IRequestHandler<GetInvoicesQuery, Result<PagedResponse<InvoiceListItem>>>
{
    public async Task<Result<PagedResponse<InvoiceListItem>>> Handle(GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = request.Status is { } contractStatus ? (DomainInvoiceStatus?)(DomainInvoiceStatus)contractStatus : null;
        return await queries.ListAsync(status, request.Page, request.PageSize, cancellationToken);
    }
}
