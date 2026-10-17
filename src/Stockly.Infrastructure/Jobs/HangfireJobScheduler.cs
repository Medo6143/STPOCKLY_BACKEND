using Hangfire;
using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Jobs;

namespace Stockly.Infrastructure.Jobs;

/// <summary><see cref="IBackgroundJobScheduler"/> on Hangfire. Arguments are ids only; the queue comes from the job's attribute.</summary>
internal sealed partial class HangfireJobScheduler(HangfireStorage storage, ILogger<HangfireJobScheduler> logger) : IBackgroundJobScheduler
{
    public void EnqueueProcessInvoice(Guid shopId, Guid invoiceId)
    {
        var jobId = Client().Enqueue<ProcessInvoiceJob>(job => job.RunAsync(shopId, invoiceId, CancellationToken.None));
        LogQueued(logger, nameof(ProcessInvoiceJob), invoiceId, shopId, jobId);
    }

    /// <summary>Queues <see cref="FailInvoiceProcessingJob"/> (used by <see cref="InvoiceJobFailureFilter"/>).</summary>
    public void EnqueueFailInvoiceProcessing(Guid shopId, Guid invoiceId)
    {
        var jobId = Client().Enqueue<FailInvoiceProcessingJob>(job => job.RunAsync(shopId, invoiceId, CancellationToken.None));
        LogQueued(logger, nameof(FailInvoiceProcessingJob), invoiceId, shopId, jobId);
    }

    private BackgroundJobClient Client() => new(storage.Get());

    [LoggerMessage(EventId = 60, Level = LogLevel.Information, Message = "Queued {Job} for invoice {InvoiceId} of shop {ShopId} (job {JobId})")]
    private static partial void LogQueued(ILogger logger, string job, Guid invoiceId, Guid shopId, string jobId);
}
