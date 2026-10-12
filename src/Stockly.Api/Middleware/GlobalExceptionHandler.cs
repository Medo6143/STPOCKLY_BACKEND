using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;

namespace Stockly.Api.Middleware;

/// <summary>
/// Unexpected exceptions become a 500 ProblemDetails with a <c>traceId</c> and no exception details
/// and code <c>Server.Unexpected</c> (AGENTS.md rule 28); title and detail are localized by
/// <see cref="ProblemDetailsEnricher"/>. Expected failures never get here: they are <c>Result</c> errors.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public const string Title = "An unexpected error occurred.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = Title,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        };
        problem.Extensions[StocklyProblems.CodeExtension] = StocklyProblems.ServerUnexpected;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
