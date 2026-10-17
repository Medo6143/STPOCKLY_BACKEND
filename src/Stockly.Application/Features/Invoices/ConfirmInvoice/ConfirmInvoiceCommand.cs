using MediatR;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Invoices.ConfirmInvoice;

/// <summary>
/// I5: receives the stock of every line of a reviewed invoice in one transaction, creates its new products and learns its
/// aliases. Idempotent: a confirmed invoice is returned unchanged (contract §1.6). Not wrapped in the transaction
/// behavior: the handler owns its transaction so the cache work runs only after the commit (docs/design/backend.md §11
/// step 11) and the last-safety-net unique violation can be answered after the rollback (step 10).
/// </summary>
public sealed record ConfirmInvoiceCommand(Guid InvoiceId) : IRequest<Result<InvoiceDetail>>;
