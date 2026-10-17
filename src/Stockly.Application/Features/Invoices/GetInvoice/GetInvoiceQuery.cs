using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Invoices;

namespace Stockly.Application.Features.Invoices.GetInvoice;

/// <summary>I3: the invoice with its lines. Another shop's invoice is <c>Invoice.NotFound</c> (AGENTS.md rule 10).</summary>
public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceDetail>;
