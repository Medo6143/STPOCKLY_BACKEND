using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Stockly.Application.Abstractions.Ai;
using Stockly.Infrastructure.Ai.OpenRouter.Dtos;

namespace Stockly.Infrastructure.Ai.OpenRouter;

/// <summary>
/// The OpenRouter stand-in of the load test (docs/operations/load-test.md): the primary handler of the OpenRouter client
/// when <c>Ai__UseFake=true</c>, which the Api accepts only in the <c>LoadTest</c> environment. Nothing leaves the
/// process: every request gets the same small, valid extraction (3 lines, consistent total) with token usage and zero
/// cost, so the rest of the pipeline (validation, matching, usage records, status cache) runs for real.
/// <paramref name="latency"/> (<c>Ai__FakeLatencyMs</c>, 0 to 20000) stands for the model's think time, so scans stay
/// <c>Processing</c> for a while as with the real model.
/// </summary>
public sealed class FakeOpenRouterHandler(TimeSpan latency) : HttpMessageHandler
{
    /// <summary>The model reported when the request names none.</summary>
    public const string DefaultModel = "fake/load-test-model";

    /// <summary>Configuration key of the simulated model latency in milliseconds.</summary>
    public const string LatencyKey = "Ai:FakeLatencyMs";

    private const int MaxLatencyMs = 20_000;

    private static readonly ExtractedInvoice Extraction = new(
        "شركة الأدوية المتحدة",
        "LT-1",
        "2026-10-10",
        "425.00",
        false,
        [
            new ExtractedInvoiceLine("بنادول اكسترا", "علبة", "10", "12.50", "125.00", null, null),
            new ExtractedInvoiceLine("Congestal", "شريط", "20", "7.50", "150.00", null, null),
            new ExtractedInvoiceLine("بروفين 400", "علبة", "10", "15.00", "150.00", null, null),
        ]);

    /// <summary><see cref="LatencyKey"/>, 0 when unset; fails start-up naming the variable when not 0..20000.</summary>
    public static TimeSpan ReadLatency(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[LatencyKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.Zero;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds) && milliseconds <= MaxLatencyMs
            ? TimeSpan.FromMilliseconds(milliseconds)
            : throw new InvalidOperationException($"Ai__FakeLatencyMs must be a whole number of milliseconds from 0 to {MaxLatencyMs}.");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (latency > TimeSpan.Zero)
        {
            await Task.Delay(latency, cancellationToken);
        }

        var model = DefaultModel;
        if (request.Content is not null)
        {
            using var body = await JsonDocument.ParseAsync(
                await request.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (body.RootElement.TryGetProperty("model", out var requested) && requested.GetString() is { Length: > 0 } name)
            {
                model = name;
            }
        }

        var answer = new OpenRouterChatResponse(
            "fake-" + Guid.NewGuid().ToString("N"),
            model,
            [new OpenRouterChoice(new OpenRouterResponseMessage("assistant", JsonSerializer.Serialize(Extraction, AiJson.Options)), "stop")],
            new OpenRouterUsage(PromptTokens: 1000, CompletionTokens: 200, TotalTokens: 1200, Cost: 0m),
            Error: null);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
    }
}
