using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;

namespace Stockly.Application.Features.Admin.GetFailedInvoices;

/// <summary>AD17: a page of failed scans of every shop (or of <see cref="ShopId"/>), newest first.</summary>
public sealed record GetFailedInvoicesQuery(Guid? ShopId, int Page, int PageSize) : IQuery<PagedResponse<AdminFailedInvoice>>;
