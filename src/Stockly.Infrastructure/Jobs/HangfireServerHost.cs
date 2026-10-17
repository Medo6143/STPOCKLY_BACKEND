using System.Data.Common;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Runs the Hangfire server inside the Api process (docs/design/backend.md §4.1): queue <c>invoices</c>,
/// <c>Hangfire:WorkerCount</c> workers, DI-scoped jobs. It waits for PostgreSQL in the background instead of failing
/// startup, so the Api serves (and reports itself unready) while the database is down.
/// </summary>
public sealed partial class HangfireServerHost(
    HangfireStorage storage,
    HangfireOptions options,
    JobActivator activator,
    IJobFilterProvider filters,
    ILogger<HangfireServerHost> logger) : BackgroundService
{
    /// <summary>Wait between two attempts to reach PostgreSQL.</summary>
    public static readonly TimeSpan StorageRetryDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Never block host startup: connecting happens after the first yield.
        await Task.Yield();

        var jobStorage = await WaitForStorageAsync(stoppingToken);
        if (jobStorage is null)
        {
            return;
        }

        var serverOptions = new BackgroundJobServerOptions
        {
            Queues = [HangfireOptions.InvoicesQueue],
            WorkerCount = options.WorkerCount,
            Activator = activator,
            FilterProvider = filters,
        };
        using var server = new BackgroundJobServer(serverOptions, jobStorage);
        LogStarted(logger, options.WorkerCount, HangfireOptions.InvoicesQueue);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            server.SendStop();
        }
    }

    private async Task<JobStorage?> WaitForStorageAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                return storage.Get();
            }
            catch (Exception exception) when (exception is DbException or TimeoutException)
            {
                LogStorageUnavailable(logger, exception.GetType().Name, (int)StorageRetryDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(StorageRetryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return null;
            }
        }

        return null;
    }

    [LoggerMessage(EventId = 70, Level = LogLevel.Information, Message = "Hangfire server started with {WorkerCount} workers on queue {Queue}")]
    private static partial void LogStarted(ILogger logger, int workerCount, string queue);

    [LoggerMessage(
        EventId = 71,
        Level = LogLevel.Warning,
        Message = "Hangfire storage unavailable ({ExceptionType}); background jobs wait, retrying in {RetrySeconds} s")]
    private static partial void LogStorageUnavailable(ILogger logger, string exceptionType, int retrySeconds);
}
