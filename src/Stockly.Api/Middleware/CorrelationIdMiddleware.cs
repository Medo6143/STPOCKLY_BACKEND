using System.Text.RegularExpressions;
using Serilog.Context;

namespace Stockly.Api.Middleware;

/// <summary>
/// Accepts the caller's <c>X-Correlation-Id</c> when it is 1–64 characters of <c>[A-Za-z0-9._-]</c>, otherwise
/// issues a new one. The id is echoed on the response, added to every log event and to ProblemDetails.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogProperty = "CorrelationId";

    private static readonly object ItemKey = new();

    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName];
        var correlationId = incoming.Count == 1 && incoming[0] is { } value && AllowedValue().IsMatch(value)
            ? value
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogProperty, correlationId))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex AllowedValue();
}
