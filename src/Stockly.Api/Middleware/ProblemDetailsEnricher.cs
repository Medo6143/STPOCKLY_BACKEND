using System.Diagnostics;
using Stockly.Api.Errors;

namespace Stockly.Api.Middleware;

/// <summary>
/// Runs for every ProblemDetails response (AGENTS.md rule 28, contract §1.8): ensures a <c>code</c> (framework errors
/// get one from their status, e.g. <c>Http.NotFound</c>), localizes <c>title</c>/<c>detail</c> from the code, and adds
/// <c>traceId</c>, <c>correlationId</c> and <c>instance</c>. Safe to run more than once.
/// </summary>
public static class ProblemDetailsEnricher
{
    public static void Enrich(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var http = context.HttpContext;
        var problem = context.ProblemDetails;
        var status = problem.Status ?? http.Response.StatusCode;

        var code = problem.Extensions.TryGetValue(StocklyProblems.CodeExtension, out var existing) && existing is string text
            ? text
            : StocklyProblems.DefaultCodeFor(status);
        problem.Extensions[StocklyProblems.CodeExtension] = code;

        if (ErrorText.TryGet(code, out var title, out var detail))
        {
            problem.Title = title;
            problem.Detail = detail;
        }

        problem.Type ??= StocklyProblems.TypeFor(status);
        problem.Instance ??= $"{http.Request.Method} {http.Request.Path}";
        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
        problem.Extensions["correlationId"] = CorrelationIdMiddleware.Get(http);
    }
}
