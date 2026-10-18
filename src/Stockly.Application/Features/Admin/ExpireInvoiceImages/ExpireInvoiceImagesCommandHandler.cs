using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Admin.ExpireInvoiceImages;

/// <summary>
/// The cut-off is <see cref="InvoiceLimits.ImageRetentionDays"/> days before now: the storage lifecycle rule expires an
/// object that many days after its upload, which is the invoice's creation. Idempotent: marked invoices are skipped.
/// </summary>
public sealed class ExpireInvoiceImagesCommandHandler(IAdminInvoiceImageRetention retention, IClock clock)
    : IRequestHandler<ExpireInvoiceImagesCommand, Result<int>>
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(InvoiceLimits.ImageRetentionDays);

    public async Task<Result<int>> Handle(ExpireInvoiceImagesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        return await retention.MarkImagesDeletedAsync(now - Retention, now, cancellationToken);
    }
}
