using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Stockly.Api.Middleware;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;

namespace Stockly.Api.Errors;

/// <summary>Builds the contract's ProblemDetails (§1.8): status, stable <c>code</c>, optional <c>errors</c>, localized text.</summary>
public static class StocklyProblems
{
    public const string ContentType = "application/problem+json";
    public const string CodeExtension = "code";
    public const string ErrorsExtension = "errors";
    public const string ServerUnexpected = "Server.Unexpected";

    private const string HttpCodePrefix = "Http.";

    private static readonly Dictionary<int, string> TypeByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        [StatusCodes.Status401Unauthorized] = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        [StatusCodes.Status403Forbidden] = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        [StatusCodes.Status404NotFound] = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        [StatusCodes.Status405MethodNotAllowed] = "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        [StatusCodes.Status409Conflict] = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        [StatusCodes.Status415UnsupportedMediaType] = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        [StatusCodes.Status500InternalServerError] = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };

    public static ProblemDetails Create(HttpContext httpContext, int status, string code, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails { Status = status };
        problem.Extensions[CodeExtension] = code;
        if (errors is not null)
        {
            problem.Extensions[ErrorsExtension] = errors;
        }

        ProblemDetailsEnricher.Enrich(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        return problem;
    }

    public static ProblemDetails FromError(HttpContext httpContext, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Create(httpContext, StatusFor(error.Type), error.Code, error.FieldErrors);
    }

    /// <summary>Contract §1.8: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, Failure 500.</summary>
    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>The code of a problem raised without one (framework errors): <c>Http.NotFound</c>, <c>Auth.Unauthenticated</c>...</summary>
    public static string DefaultCodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ValidationFailure.Code,
        StatusCodes.Status401Unauthorized => AuthErrors.Unauthenticated.Code,
        StatusCodes.Status403Forbidden => AuthErrors.Forbidden.Code,
        >= StatusCodes.Status500InternalServerError => ServerUnexpected,
        _ => HttpCodePrefix + ReasonPhrases.GetReasonPhrase(status).Replace(" ", string.Empty, StringComparison.Ordinal),
    };

    public static string? TypeFor(int status) => TypeByStatus.GetValueOrDefault(status);
}
