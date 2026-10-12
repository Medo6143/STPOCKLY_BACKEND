using System.Diagnostics;

namespace Stockly.Api.Middleware;

/// <summary>Adds <c>traceId</c>, <c>correlationId</c> and <c>instance</c> to every ProblemDetails response (AGENTS.md rule 28).</summary>
public static class ProblemDetailsEnricher
{
    public static void Enrich(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var http = context.HttpContext;
        context.ProblemDetails.Instance ??= $"{http.Request.Method} {http.Request.Path}";
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
        context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(http);
    }
}
