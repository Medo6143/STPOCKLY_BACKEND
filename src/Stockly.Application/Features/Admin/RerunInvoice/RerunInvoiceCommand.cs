using MediatR;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.RerunInvoice;

/// <summary>
/// AD19: puts a <c>Failed</c> invoice back to <c>Processing</c> and queues its extraction again, bypassing the AI result
/// cache and the reuse of an earlier extraction. Quota is not charged. Not an <c>ICommand</c>: the job is queued only
/// after the transaction commits, or it could read the invoice while it is still <c>Failed</c>.
/// </summary>
public sealed record RerunInvoiceCommand(Guid InvoiceId, string? Reason) : IRequest<Result<InvoiceDetail>>;
