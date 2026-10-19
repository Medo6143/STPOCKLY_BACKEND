using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Stockly.Application.Features.Admin.ReconcilePayments;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Recurring Hangfire job (ADR-0015 §4): sends <see cref="ReconcilePaymentsCommand"/>, which settles pending payments
/// past their expiry with the provider. No tenant: the command is platform-wide. Logs only counts.
/// </summary>
public sealed partial class ReconcilePaymentsJob(ISender sender, ILogger<ReconcilePaymentsJob> logger)
{
    /// <summary>The recurring job's id in Hangfire.</summary>
    public const string RecurringJobId = "payment-reconcile";

    /// <summary>Every 15 minutes.</summary>
    public const string Cron = "*/15 * * * *";

    [Queue(HangfireOptions.MaintenanceQueue)]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReconcilePaymentsCommand(), cancellationToken);
        if (result.IsFailure)
        {
            LogFailed(logger, result.Error.Code);
            return;
        }

        var summary = result.Value;
        LogReconciled(logger, summary.Checked, summary.Paid, summary.Expired, summary.Failed, summary.Skipped);
    }

    [LoggerMessage(
        EventId = 90,
        Level = LogLevel.Information,
        Message = "Payment reconcile checked {Checked}: {Paid} paid, {Expired} expired, {Failed} failed, {Skipped} skipped")]
    private static partial void LogReconciled(ILogger logger, int @checked, int paid, int expired, int failed, int skipped);

    [LoggerMessage(EventId = 91, Level = LogLevel.Warning, Message = "Payment reconcile failed with {Code}")]
    private static partial void LogFailed(ILogger logger, string code);
}
