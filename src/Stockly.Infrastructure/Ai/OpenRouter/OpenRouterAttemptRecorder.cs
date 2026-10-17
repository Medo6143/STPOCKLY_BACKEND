using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Stockly.Domain.Platform;

namespace Stockly.Infrastructure.Ai.OpenRouter;

/// <summary>
/// Innermost handler of the OpenRouter client (docs/design/backend.md §6.3): times every HTTP attempt, including the
/// retries the resilience handler makes, and records its outcome in the request's <see cref="AiAttemptLog"/>. Logs the
/// model, attempt number, status, outcome and latency only: never the key, headers, body or image (rule 12).
/// </summary>
internal sealed partial class OpenRouterAttemptRecorder(ILogger<OpenRouterAttemptRecorder> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Options.TryGetValue(AiAttemptLog.Key, out var log))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            Record(log, OutcomeFor(response.StatusCode), (int)response.StatusCode, started);
            return response;
        }
        catch (HttpRequestException)
        {
            Record(log, AiCallOutcome.NetworkError, null, started);
            throw;
        }
        catch (OperationCanceledException)
        {
            Record(log, log.JobCancellation.IsCancellationRequested ? AiCallOutcome.Cancelled : AiCallOutcome.Timeout, null, started);
            throw;
        }
    }

    private static AiCallOutcome OutcomeFor(HttpStatusCode status) => (int)status switch
    {
        >= 200 and < 300 => AiCallOutcome.Succeeded,
        429 => AiCallOutcome.RateLimited,
        >= 500 => AiCallOutcome.ServerError,
        _ => AiCallOutcome.ClientError,
    };

    private void Record(AiAttemptLog log, AiCallOutcome outcome, int? status, long started)
    {
        var latency = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var attempt = log.Add(outcome, status, latency);
        LogAttempt(logger, log.Model, attempt, status, outcome, latency);
    }

    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Information,
        Message = "OpenRouter attempt {Attempt} with {Model}: HTTP {HttpStatus}, {Outcome} in {LatencyMs} ms")]
    private static partial void LogAttempt(ILogger logger, string model, int attempt, int? httpStatus, AiCallOutcome outcome, int latencyMs);
}
