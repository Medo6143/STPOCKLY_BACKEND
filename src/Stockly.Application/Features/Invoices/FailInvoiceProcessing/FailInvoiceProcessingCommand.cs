using MediatR;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Invoices.FailInvoiceProcessing;

/// <summary>
/// The processing job failed for an unexpected reason after all its retries (docs/design/backend.md §4.1): mark the
/// invoice <c>Failed</c> with <c>Unexpected</c> so the app stops polling. Sent by the job failure filter inside the
/// invoice's tenant scope. An invoice that is no longer processing is left as it is (<c>Invoice.NotProcessing</c>).
/// </summary>
public sealed record FailInvoiceProcessingCommand(Guid InvoiceId) : IRequest<Result>;
