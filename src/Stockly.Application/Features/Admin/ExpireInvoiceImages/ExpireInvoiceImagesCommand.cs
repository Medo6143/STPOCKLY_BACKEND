using Stockly.Application.Abstractions.Messaging;

namespace Stockly.Application.Features.Admin.ExpireInvoiceImages;

/// <summary>
/// The daily image retention run (docs/operations/image-lifecycle.md): marks the images older than
/// <c>InvoiceLimits.ImageRetentionDays</c> as deleted, so AD18 answers <c>imageUrl: null</c> for them. A system job,
/// not an admin action: no admin user and no audit entry. Returns the number of invoices marked.
/// </summary>
public sealed record ExpireInvoiceImagesCommand : ICommand<int>;
