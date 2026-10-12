namespace Stockly.Api.Errors;

/// <summary>Writes a contract ProblemDetails outside MVC (authentication and authorization failures).</summary>
public sealed class ProblemWriter(IProblemDetailsService problemDetailsService)
{
    public async Task WriteAsync(HttpContext httpContext, int status, string code)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = status;
        var problem = StocklyProblems.Create(httpContext, status, code);
        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(problem, options: null, StocklyProblems.ContentType, httpContext.RequestAborted);
        }
    }
}
