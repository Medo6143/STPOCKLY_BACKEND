using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Stockly.Application.Features.Admin.DowngradeLapsedSubscriptions;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Daily Hangfire recurring job (ADR-0015 §5): sends <see cref="DowngradeLapsedSubscriptionsCommand"/>, which moves shops
/// whose paid month ended to the default free plan. A system action: no admin audit entry. Logs only the count.
/// </summary>
public sealed partial class DowngradeLapsedSubscriptionsJob(ISender sender, ILogger<DowngradeLapsedSubscriptionsJob> logger)
{
    /// <summary>The recurring job's id in Hangfire.</summary>
    public const string RecurringJobId = "subscription-downgrade";

    /// <summary>Every day at 22:10 UTC (00:10 or 01:10 Cairo), just after the Cairo day changed.</summary>
    public const string Cron = "10 22 * * *";

    [Queue(HangfireOptions.MaintenanceQueue)]
    [AutomaticRetry(Attempts = 3)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DowngradeLapsedSubscriptionsCommand(), cancellationToken);
        if (result.IsFailure)
        {
            LogFailed(logger, result.Error.Code);
            return;
        }

        LogDowngraded(logger, result.Value);
    }

    [LoggerMessage(EventId = 92, Level = LogLevel.Information, Message = "Subscription downgrade moved {Count} shops to the default plan")]
    private static partial void LogDowngraded(ILogger logger, int count);

    [LoggerMessage(EventId = 93, Level = LogLevel.Warning, Message = "Subscription downgrade failed with {Code}")]
    private static partial void LogFailed(ILogger logger, string code);
}
