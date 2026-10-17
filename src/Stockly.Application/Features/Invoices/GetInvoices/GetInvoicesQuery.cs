using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;

namespace Stockly.Application.Features.Invoices.GetInvoices;

/// <summary>I2: a page of the shop's invoices, newest first, optionally of one status.</summary>
public sealed record GetInvoicesQuery(InvoiceStatus? Status, int Page, int PageSize) : IQuery<PagedResponse<InvoiceListItem>>;
