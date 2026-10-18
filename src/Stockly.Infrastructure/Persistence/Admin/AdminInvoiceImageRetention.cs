using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Domain.Invoices;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>
/// The image retention write across every shop (docs/operations/image-lifecycle.md, ADR-0005 update 2026-10-18: cross-
/// tenant access lives only in this folder). One <c>UPDATE</c> served by <c>ix_invoices_created_at_image_kept</c>
/// (created_at) WHERE image_deleted_at IS NULL.
/// </summary>
internal sealed class AdminInvoiceImageRetention(StocklyDbContext db) : IAdminInvoiceImageRetention
{
    public Task<int> MarkImagesDeletedAsync(DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<Invoice>()
            .IgnoreQueryFilters()
            .Where(invoice => invoice.ImageDeletedAt == null && invoice.CreatedAt < createdBefore)
            .ExecuteUpdateAsync(setters => setters.SetProperty(invoice => invoice.ImageDeletedAt, now), cancellationToken);
}
