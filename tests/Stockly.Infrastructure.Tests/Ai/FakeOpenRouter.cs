using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stockly.Infrastructure.Tests.Ai;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> in place of OpenRouter: tests never call the real API. Each model gets a
/// queue of answers, used in order (the last one repeats). Records every request it received.
/// </summary>
internal sealed class FakeOpenRouter : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<CancellationToken, Task<HttpResponseMessage>>>> _answers = [];
    private readonly Dictionary<string, Func<CancellationToken, Task<HttpResponseMessage>>> _last = [];

    public List<RecordedRequest> Requests { get; } = [];

    public FakeOpenRouter Answer(string model, params Func<CancellationToken, Task<HttpResponseMessage>>[] answers)
    {
        _answers[model] = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(answers);
        _last[model] = answers[^1];
        return this;
    }

    public static Func<CancellationToken, Task<HttpResponseMessage>> Completion(
        string? content, string finishReason = "stop", int promptTokens = 1000, int completionTokens = 200, decimal? cost = 0.00042m) =>
        _ => Task.FromResult(Json(HttpStatusCode.OK, new
        {
            id = "gen-1",
            model = "answering-model",
            choices = new[] { new { message = new { role = "assistant", content }, finish_reason = finishReason } },
            usage = new { prompt_tokens = promptTokens, completion_tokens = completionTokens, total_tokens = promptTokens + completionTokens, cost },
        }));

    public static Func<CancellationToken, Task<HttpResponseMessage>> Status(HttpStatusCode status) =>
        _ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("{\"error\":{\"message\":\"secret detail\"}}") };

            // Retry-After: 0 keeps the retry tests fast; the backoff itself is the pipeline's.
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(response);
        };

    public static Func<CancellationToken, Task<HttpResponseMessage>> Body(string body) =>
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    public static Func<CancellationToken, Task<HttpResponseMessage>> Hang() =>
        async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable.");
        };

    public static Func<CancellationToken, Task<HttpResponseMessage>> NetworkError() =>
        _ => throw new HttpRequestException("connection refused");

    public int CountFor(string model) => Requests.Count(request => request.Model == model);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var json = JsonNode.Parse(body)!;
        var model = json["model"]!.GetValue<string>();
        lock (Requests)
        {
            Requests.Add(new RecordedRequest(model, json, request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
        }

        var answer = _answers.TryGetValue(model, out var queue) && queue.Count > 0 ? queue.Dequeue() : _last[model];
        return await answer(cancellationToken);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };

    internal sealed record RecordedRequest(string Model, JsonNode Body, Uri Uri, IReadOnlyDictionary<string, string> Headers);
}
