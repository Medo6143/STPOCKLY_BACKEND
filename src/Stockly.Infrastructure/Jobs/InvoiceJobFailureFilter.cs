using Hangfire.States;
using Hangfire.Storage;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// When <see cref="ProcessInvoiceJob"/> reaches Hangfire's <c>Failed</c> state (its retries are used up), queues
/// <see cref="FailInvoiceProcessingJob"/> so the invoice leaves <c>Processing</c> and the app stops polling
/// (docs/design/backend.md §4.1). Registered for this process's server only, through <see cref="StocklyJobFilterProvider"/>.
/// </summary>
internal sealed class InvoiceJobFailureFilter(HangfireJobScheduler scheduler) : IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.NewState is FailedState
            && context.BackgroundJob.Job is { } job
            && job.Type == typeof(ProcessInvoiceJob)
            && job.Args is [Guid shopId, Guid invoiceId, ..])
        {
            scheduler.EnqueueFailInvoiceProcessing(shopId, invoiceId);
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
