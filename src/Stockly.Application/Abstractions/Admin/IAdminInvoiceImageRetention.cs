namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// Platform-wide image retention (docs/operations/image-lifecycle.md): records on the invoices of every shop that the
/// storage lifecycle rule has expired their image. Cross-tenant; used only by <c>Application/Features/Admin</c>.
/// </summary>
public interface IAdminInvoiceImageRetention
{
    /// <summary>
    /// Sets <c>image_deleted_at = <paramref name="now"/></c> on every invoice created before
    /// <paramref name="createdBefore"/> whose image is not marked yet. One statement; returns the rows marked.
    /// </summary>
    Task<int> MarkImagesDeletedAsync(DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken);
}
