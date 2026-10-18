using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Stockly.Application.Features.Admin.ExpireInvoiceImages;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Daily Hangfire recurring job (docs/operations/image-lifecycle.md): sends <see cref="ExpireInvoiceImagesCommand"/>,
/// which marks the invoices whose image the MinIO lifecycle rule has expired. No tenant: the command is platform-wide.
/// Logs only the count.
/// </summary>
public sealed partial class ExpireInvoiceImagesJob(ISender sender, ILogger<ExpireInvoiceImagesJob> logger)
{
    /// <summary>The recurring job's id in Hangfire.</summary>
    public const string RecurringJobId = "invoice-image-expiry";

    /// <summary>Every day at 01:30 UTC (03:30/04:30 Cairo), after the night's scans.</summary>
    public const string Cron = "30 1 * * *";

    [Queue(HangfireOptions.MaintenanceQueue)]
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ExpireInvoiceImagesCommand(), cancellationToken);
        if (result.IsFailure)
        {
            LogFailed(logger, result.Error.Code);
            return;
        }

        LogMarked(logger, result.Value);
    }

    [LoggerMessage(EventId = 80, Level = LogLevel.Information, Message = "Image retention marked {Count} invoices as image deleted")]
    private static partial void LogMarked(ILogger logger, int count);

    [LoggerMessage(EventId = 81, Level = LogLevel.Warning, Message = "Image retention failed with {Code}")]
    private static partial void LogFailed(ILogger logger, string code);
}
