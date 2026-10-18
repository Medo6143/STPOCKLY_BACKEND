using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace Stockly.LoadTests;

/// <summary>One request of a scenario: whether it succeeded and how long it took.</summary>
internal readonly record struct Sample(bool Succeeded, double Milliseconds);

/// <summary>The measured result of a scenario.</summary>
internal sealed record ScenarioResult(string Name, IReadOnlyList<Sample> Samples, TimeSpan Elapsed)
{
    public int Count => Samples.Count;

    public int Errors => Samples.Count(sample => !sample.Succeeded);

    public double ErrorRate => Count == 0 ? 0 : (double)Errors / Count;

    public double RequestsPerSecond => Elapsed.TotalSeconds == 0 ? 0 : Count / Elapsed.TotalSeconds;

    /// <summary>Nearest-rank percentile of the latency of every request (errors included).</summary>
    public double Percentile(double percentile)
    {
        if (Count == 0)
        {
            return 0;
        }

        var sorted = Samples.Select(sample => sample.Milliseconds).Order().ToArray();
        var rank = (int)Math.Ceiling(percentile / 100 * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }

    public string ToMarkdownRow() => string.Create(
        CultureInfo.InvariantCulture,
        $"| {Name} | {Count} | {RequestsPerSecond:F1} | {Percentile(50):F1} | {Percentile(95):F1} | {Percentile(99):F1} | {ErrorRate:P2} |");
}

/// <summary>Runs a scenario with N concurrent virtual users, for a duration or for a number of requests.</summary>
internal static class ScenarioRunner
{
    public const string MarkdownHeader =
        "| Scenario | Requests | Req/s | p50 ms | p95 ms | p99 ms | Error rate |\n|---|---|---|---|---|---|---|";

    /// <summary>Each user repeats <paramref name="request"/> until <paramref name="duration"/> has passed.</summary>
    public static Task<ScenarioResult> ForDurationAsync(
        string name, int concurrency, TimeSpan duration, Func<int, Task<bool>> request)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var sequence = -1;
        return RunAsync(name, concurrency, () => Stopwatch.GetTimestamp() < deadline ? Interlocked.Increment(ref sequence) : null, request);
    }

    /// <summary>The users share <paramref name="count"/> requests, numbered 0..count-1.</summary>
    public static Task<ScenarioResult> ForCountAsync(string name, int concurrency, int count, Func<int, Task<bool>> request)
    {
        var sequence = -1;
        return RunAsync(
            name,
            concurrency,
            () =>
            {
                var next = Interlocked.Increment(ref sequence);
                return next < count ? next : null;
            },
            request);
    }

    /// <summary>A 2xx status, or one of <paramref name="expected"/>.</summary>
    public static bool IsSuccess(HttpStatusCode status, params HttpStatusCode[] expected) =>
        ((int)status >= 200 && (int)status < 300) || expected.Contains(status);

    private static async Task<ScenarioResult> RunAsync(
        string name, int concurrency, Func<int?> nextRequest, Func<int, Task<bool>> request)
    {
        var samples = new ConcurrentBag<Sample>();
        var watch = Stopwatch.StartNew();
        var users = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            while (nextRequest() is { } number)
            {
                var started = Stopwatch.GetTimestamp();
                bool succeeded;
                try
                {
                    succeeded = await request(number);
                }
                catch (HttpRequestException)
                {
                    succeeded = false;
                }
                catch (TaskCanceledException)
                {
                    succeeded = false;
                }

                samples.Add(new Sample(succeeded, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            }
        });
        await Task.WhenAll(users);
        watch.Stop();
        return new ScenarioResult(name, [.. samples], watch.Elapsed);
    }
}
