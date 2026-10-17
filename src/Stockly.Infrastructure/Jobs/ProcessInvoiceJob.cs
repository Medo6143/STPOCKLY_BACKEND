using System.Diagnostics;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Stockly.Application.Features.Invoices.ProcessInvoice;
using Stockly.Domain.Invoices;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Hangfire entry point of the invoice extraction (docs/design/backend.md §4.1). Hangfire's activator creates a DI scope
/// per run and builds this job in it; the job sets that scope's tenant, then sends <see cref="ProcessInvoiceCommand"/>.
/// Arguments are ids only (rule 12). Unexpected exceptions are retried 3 times; after the last one
/// <see cref="InvoiceJobFailureFilter"/> marks the invoice <c>Failed</c>. Hangfire replaces the token with its shutdown token.
/// </summary>
public sealed partial class ProcessInvoiceJob(JobTenantContext tenant, ISender sender, ILogger<ProcessInvoiceJob> logger)
{
    /// <summary>Hangfire runs a job at most this many times after its first failure.</summary>
    public const int RetryAttempts = 3;

    [Queue(HangfireOptions.InvoicesQueue)]
    [AutomaticRetry(Attempts = RetryAttempts, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(Guid shopId, Guid invoiceId, CancellationToken cancellationToken)
    {
        tenant.Set(shopId);
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["ShopId"] = shopId, ["InvoiceId"] = invoiceId });
        var started = Stopwatch.GetTimestamp();
        LogStarted(logger, invoiceId, shopId);

        try
        {
            var result = await sender.Send(new ProcessInvoiceCommand(invoiceId), cancellationToken);
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (result.IsFailure)
            {
                LogSkipped(logger, invoiceId, result.Error.Code, elapsed);
                return;
            }

            var outcome = result.Value;
            LogFinished(
                logger,
                invoiceId,
                outcome.Status,
                outcome.FailureReason,
                outcome.Warnings,
                outcome.LineCount,
                outcome.AiUsageRecordCount,
                elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCrashed(logger, exception, invoiceId, exception.GetType().Name);
            throw;
        }
    }

    [LoggerMessage(EventId = 50, Level = LogLevel.Information, Message = "Processing invoice {InvoiceId} of shop {ShopId}")]
    private static partial void LogStarted(ILogger logger, Guid invoiceId, Guid shopId);

    [LoggerMessage(
        EventId = 51,
        Level = LogLevel.Information,
        Message = "Invoice {InvoiceId} is {Status} (failure {FailureReason}, warnings {Warnings}, {LineCount} lines, {AiUsageRecords} AI usage rows) in {DurationMs} ms")]
    private static partial void LogFinished(
        ILogger logger,
        Guid invoiceId,
        InvoiceStatus status,
        InvoiceFailureReason? failureReason,
        IReadOnlyList<InvoiceWarning> warnings,
        int lineCount,
        int aiUsageRecords,
        long durationMs);

    [LoggerMessage(EventId = 52, Level = LogLevel.Information, Message = "Invoice {InvoiceId} skipped ({Code}) in {DurationMs} ms")]
    private static partial void LogSkipped(ILogger logger, Guid invoiceId, string code, long durationMs);

    [LoggerMessage(EventId = 53, Level = LogLevel.Error, Message = "Processing invoice {InvoiceId} failed with {ExceptionType}; Hangfire retries it")]
    private static partial void LogCrashed(ILogger logger, Exception exception, Guid invoiceId, string exceptionType);
}
