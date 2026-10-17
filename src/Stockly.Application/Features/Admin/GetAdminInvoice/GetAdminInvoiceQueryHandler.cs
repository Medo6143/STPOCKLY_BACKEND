using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Storage;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Admin.GetAdminInvoice;

public sealed class GetAdminInvoiceQueryHandler(IAdminInvoiceQueries invoices, IFileStorage storage)
    : IRequestHandler<GetAdminInvoiceQuery, Result<AdminInvoiceDetail>>
{
    public async Task<Result<AdminInvoiceDetail>> Handle(GetAdminInvoiceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record = await invoices.FindAsync(request.InvoiceId, cancellationToken);
        if (record is null)
        {
            return InvoiceErrors.NotFound;
        }

        var imageUrl = await storage.GetDownloadUrlAsync(record.ImageObjectKey, AdminLimits.ImageUrlLifetime, cancellationToken);
        return new AdminInvoiceDetail(record.Invoice, record.ShopId, record.ShopName, imageUrl, record.RawAiJson, record.Usage);
    }
}
