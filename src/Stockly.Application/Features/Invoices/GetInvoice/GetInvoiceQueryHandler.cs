using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Application.Features.Invoices.GetInvoice;

/// <summary>
/// I3 (docs/design/backend.md §2.2, §9.2). A poll of a processing invoice is answered from the status cache with no
/// PostgreSQL query; anything else (miss, Redis down, any other status) reads the projection. A processing invoice read
/// from PostgreSQL is written back to the cache (read-through, §17.1 item 5). The cache key holds the caller's shop, so a
/// hit is always the caller's own invoice.
/// </summary>
public sealed class GetInvoiceQueryHandler(
    ICurrentTenant currentTenant,
    IInvoiceStatusCache statusCache,
    IInvoiceQueries queries) : IRequestHandler<GetInvoiceQuery, Result<InvoiceDetail>>
{
    public async Task<Result<InvoiceDetail>> Handle(GetInvoiceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentTenant.ShopId is not { } shopId)
        {
            return InvoiceErrors.NotFound;
        }

        var snapshot = await statusCache.GetAsync(shopId, request.InvoiceId, cancellationToken);
        if (snapshot is { Status: DomainInvoiceStatus.Processing } && snapshot.InvoiceId == request.InvoiceId)
        {
            return InvoiceMapping.ToProcessingDetail(snapshot);
        }

        var detail = await queries.FindDetailAsync(request.InvoiceId, cancellationToken);
        if (detail is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (detail.Status == Contracts.Invoices.InvoiceStatus.Processing)
        {
            await statusCache.SetAsync(shopId, InvoiceMapping.ToStatusSnapshot(detail), cancellationToken);
        }

        return detail;
    }
}
