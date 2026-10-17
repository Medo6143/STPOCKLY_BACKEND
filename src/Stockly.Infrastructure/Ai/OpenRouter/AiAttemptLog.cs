using Stockly.Domain.Platform;

namespace Stockly.Infrastructure.Ai.OpenRouter;

/// <summary>
/// The HTTP attempts of one model call. The extractor puts it in <see cref="HttpRequestMessage.Options"/>; the resilience
/// handler resends the same request message on a retry, so <see cref="OpenRouterAttemptRecorder"/> (innermost) sees and
/// records every attempt here. <see cref="JobCancellation"/> tells a timeout apart from a stopped job.
/// </summary>
internal sealed class AiAttemptLog(string model, CancellationToken jobCancellation)
{
    public static readonly HttpRequestOptionsKey<AiAttemptLog> Key = new("Stockly.AiAttemptLog");

    private readonly List<Entry> _entries = [];

    public string Model { get; } = model;

    public CancellationToken JobCancellation { get; } = jobCancellation;

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public int Add(AiCallOutcome outcome, int? httpStatus, int latencyMs)
    {
        lock (_entries)
        {
            _entries.Add(new Entry(outcome, httpStatus, latencyMs));
            return _entries.Count;
        }
    }

    /// <summary>One attempt as the transport saw it.</summary>
    public sealed record Entry(AiCallOutcome Outcome, int? HttpStatus, int LatencyMs);
}
