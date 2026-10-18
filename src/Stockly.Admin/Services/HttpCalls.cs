using System.Net.Http.Json;
using System.Text.Json;

namespace Stockly.Admin.Services;

/// <summary>
/// Sends one request and turns the response into an <see cref="ApiResult{T}"/>: the JSON body on success, the
/// ProblemDetails otherwise, and a client-side problem when the API cannot be reached or answers something unreadable.
/// </summary>
public static class HttpCalls
{
    public static Task<ApiResult<T>> GetAsync<T>(HttpClient http, string path, CancellationToken cancellationToken) =>
        SendAsync<T>(http, new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    public static Task<ApiResult<T>> PostAsync<T>(HttpClient http, string path, object? body, CancellationToken cancellationToken) =>
        SendAsync<T>(http, WithBody(HttpMethod.Post, path, body), cancellationToken);

    public static Task<ApiResult<T>> PutAsync<T>(HttpClient http, string path, object body, CancellationToken cancellationToken) =>
        SendAsync<T>(http, WithBody(HttpMethod.Put, path, body), cancellationToken);

    private static HttpRequestMessage WithBody(HttpMethod method, string path, object? body) => new(method, path)
    {
        Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: ApiJson.Options),
    };

    private static async Task<ApiResult<T>> SendAsync<T>(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return ApiResult.Failure<T>(await ProblemReader.ReadAsync(response, cancellationToken));
                }

                if (typeof(T) == typeof(NoContent))
                {
                    return ApiResult.Success<T>(default!);
                }

                var value = await response.Content.ReadFromJsonAsync<T>(ApiJson.Options, cancellationToken);
                return value is null
                    ? ApiResult.Failure<T>(ApiProblem.InvalidResponse(response.StatusCode))
                    : ApiResult.Success<T>(value);
            }
            catch (HttpRequestException)
            {
                return ApiResult.Failure<T>(ApiProblem.Unreachable());
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient's own timeout; a cancellation by the caller (page left) propagates and Blazor ignores it.
                return ApiResult.Failure<T>(ApiProblem.Unreachable());
            }
            catch (JsonException)
            {
                return ApiResult.Failure<T>(ApiProblem.InvalidResponse(System.Net.HttpStatusCode.OK));
            }
        }
    }
}
