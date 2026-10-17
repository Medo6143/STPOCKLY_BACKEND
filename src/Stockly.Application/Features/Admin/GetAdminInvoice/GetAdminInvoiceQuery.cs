using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.GetAdminInvoice;

/// <summary>
/// AD18: an invoice of any shop with a 5-minute pre-signed image URL, the raw AI output and its AI usage rows. The raw
/// output is returned to the admin and never logged (rule 12).
/// </summary>
public sealed record GetAdminInvoiceQuery(Guid InvoiceId) : IQuery<AdminInvoiceDetail>;
