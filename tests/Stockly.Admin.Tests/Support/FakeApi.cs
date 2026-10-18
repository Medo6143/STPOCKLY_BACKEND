using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Stockly.Admin.Services;

namespace Stockly.Admin.Tests.Support;

/// <summary>
/// A fake Stockly API behind an <see cref="HttpMessageHandler"/>: routes are "METHOD path" (query ignored), answers are
/// queued per route (the last one repeats). Every request is recorded with its bearer token and body.
/// </summary>
public sealed class FakeApi : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("http://api.test/");

    private readonly ConcurrentDictionary<string, Queue<Func<Task<HttpResponseMessage>>>> _routes = new(StringComparer.Ordinal);

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public FakeApi On(HttpMethod method, string path, Func<Task<HttpResponseMessage>> respond)
    {
        _routes.GetOrAdd(Key(method, path), _ => new Queue<Func<Task<HttpResponseMessage>>>()).Enqueue(respond);
        return this;
    }

    public FakeApi On(HttpMethod method, string path, HttpStatusCode status, object? body = null) =>
        On(method, path, () => Task.FromResult(Json(status, body)));

    public FakeApi OnGet(string path, object body) => On(HttpMethod.Get, path, HttpStatusCode.OK, body);

    /// <summary>The request never completes: the page stays in its loading state.</summary>
    public FakeApi Pending(HttpMethod method, string path) =>
        On(method, path, () => new TaskCompletionSource<HttpResponseMessage>().Task);

    public FakeApi Problem(HttpMethod method, string path, HttpStatusCode status, string code, string detail, string traceId) =>
        On(method, path, status, new { title = status.ToString(), status = (int)status, detail, code, traceId });

    public IEnumerable<RecordedRequest> To(HttpMethod method, string path) =>
        Requests.Where(request => request.Method == method && request.Path == path);

    public static HttpResponseMessage Json(HttpStatusCode status, object? body) => new(status)
    {
        Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: ApiJson.Options),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(request.Method, path, request.RequestUri.Query, request.Headers.Authorization?.Parameter, body));

        if (!_routes.TryGetValue(Key(request.Method, path), out var answers) || answers.Count == 0)
        {
            return Json(HttpStatusCode.NotFound, new { title = "Not Found", status = 404, code = "Http.NotFound" });
        }

        var respond = answers.Count > 1 ? answers.Dequeue() : answers.Peek();
        return await respond();
    }

    private static string Key(HttpMethod method, string path) => $"{method.Method} {path}";
}

public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? BearerToken, string? Body);
