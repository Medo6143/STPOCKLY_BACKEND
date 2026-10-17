using Hangfire;
using MediatR;
using Stockly.Application.Features.Invoices.FailInvoiceProcessing;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Marks an invoice <c>Failed</c> with <c>Unexpected</c> after <see cref="ProcessInvoiceJob"/> used up its retries
/// (queued by <see cref="InvoiceJobFailureFilter"/>). A job of its own, so the work runs in a DI scope with the tenant
/// set, like every job, instead of inside Hangfire's state change.
/// </summary>
public sealed class FailInvoiceProcessingJob(JobTenantContext tenant, ISender sender)
{
    [Queue(HangfireOptions.InvoicesQueue)]
    [AutomaticRetry(Attempts = ProcessInvoiceJob.RetryAttempts, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(Guid shopId, Guid invoiceId, CancellationToken cancellationToken)
    {
        tenant.Set(shopId);
        await sender.Send(new FailInvoiceProcessingCommand(invoiceId), cancellationToken);
    }
}
