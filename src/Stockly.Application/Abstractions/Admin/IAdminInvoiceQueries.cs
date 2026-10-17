using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>Cross-tenant invoice access for the platform admin (AD17–AD19). Used only by <c>Application/Features/Admin</c>.</summary>
public interface IAdminInvoiceQueries
{
    /// <summary>AD17: a page of failed invoices of every shop (or of <paramref name="shopId"/>), newest first.</summary>
    Task<PagedResponse<AdminFailedInvoice>> ListFailedAsync(Guid? shopId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>AD18: the invoice of any shop with its image key, raw AI output and usage rows; <c>null</c> when missing.</summary>
    Task<AdminInvoiceRecord?> FindAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// AD19: locks the invoice row of any shop (<c>FOR UPDATE</c>, inside a transaction) and loads it tracked, without
    /// its lines; <c>null</c> when it does not exist.
    /// </summary>
    Task<Invoice?> FindForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken);
}

/// <summary>An invoice as the admin sees it, before the image URL is signed.</summary>
public sealed record AdminInvoiceRecord(
    InvoiceDetail Invoice,
    Guid ShopId,
    string ShopName,
    string ImageObjectKey,
    string? RawAiJson,
    IReadOnlyList<AdminAiUsageDto> Usage);
