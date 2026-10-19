using System.Globalization;
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
        [StatusCodes.Status413PayloadTooLarge] = "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        [StatusCodes.Status415UnsupportedMediaType] = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        [StatusCodes.Status423Locked] = "https://tools.ietf.org/html/rfc4918#section-11.3",
        [StatusCodes.Status429TooManyRequests] = "https://tools.ietf.org/html/rfc6585#section-4",
        [StatusCodes.Status500InternalServerError] = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        [StatusCodes.Status502BadGateway] = "https://tools.ietf.org/html/rfc9110#section-15.6.3",
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

    /// <summary>The problem for <paramref name="error"/>. Also sets the <c>Retry-After</c> header when the error carries one.</summary>
    public static ProblemDetails FromError(HttpContext httpContext, Error error)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(error);

        if (error.RetryAfter is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter = RetryAfterSeconds(retryAfter).ToString(CultureInfo.InvariantCulture);
        }

        return Create(httpContext, StatusFor(error.Type), error.Code, error.FieldErrors);
    }

    /// <summary>
    /// Contract §1.8: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, Failure 500;
    /// v1.4: Locked 423, TooManyRequests 429; PayloadTooLarge 413 (I1, backend.md §13); v1.7: BadGateway 502 (B3).
    /// </summary>
    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Locked => StatusCodes.Status423Locked,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorType.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorType.BadGateway => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary><c>Retry-After</c> value (contract §1.4): whole seconds, rounded up, at least 1.</summary>
    public static long RetryAfterSeconds(TimeSpan retryAfter) =>
        Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));

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
