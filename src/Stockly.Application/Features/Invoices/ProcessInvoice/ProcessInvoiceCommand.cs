using MediatR;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.ProcessInvoice;

/// <summary>
/// The background job's use case (docs/design/backend.md §4.2): turn a <c>Processing</c> invoice into
/// <c>NeedsReview</c> or <c>Failed</c>. Sent by <c>ProcessInvoiceJob</c> inside the invoice's tenant scope, never from
/// an HTTP request (AGENTS.md rule 14). Idempotent: an invoice that is no longer processing returns
/// <c>Invoice.NotProcessing</c> and nothing is written. Manages its own transaction (the final save locks the invoice).
/// <see cref="BypassAiCache"/> (the admin's re-run, contract AD19) skips the reuse of an earlier extraction and the AI
/// result cache, so OpenRouter is called; every call still writes an <c>AiUsageRecord</c> (rule 18).
/// </summary>
public sealed record ProcessInvoiceCommand(Guid InvoiceId, bool BypassAiCache = false) : IRequest<Result<ProcessInvoiceOutcome>>;

/// <summary>What the job did, for its log line: the new status, the failure reason, warnings, lines and AI usage rows.</summary>
public sealed record ProcessInvoiceOutcome(
    InvoiceStatus Status,
    InvoiceFailureReason? FailureReason,
    IReadOnlyList<InvoiceWarning> Warnings,
    int LineCount,
    int AiUsageRecordCount);
