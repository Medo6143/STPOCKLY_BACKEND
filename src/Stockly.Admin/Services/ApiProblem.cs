using System.Net;

namespace Stockly.Admin.Services;

/// <summary>
/// An RFC 7807 ProblemDetails from the API (contract §1.8), or a browser-side failure shaped the same way, so every page
/// shows errors one way: <see cref="Message"/> for people, <see cref="Code"/> for code, <see cref="TraceId"/> for support.
/// </summary>
public sealed record ApiProblem(
    int Status,
    string Code,
    string Title,
    string? Detail,
    string? TraceId,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    /// <summary>The text to show: the server's <c>detail</c>, else its <c>title</c>.</summary>
    public string Message => string.IsNullOrWhiteSpace(Detail) ? Title : Detail;

    /// <summary>One line for a snackbar: the message, and the trace id when the server gave one.</summary>
    public string OneLine => TraceId is null ? Message : $"{Message} (trace id {TraceId})";

    /// <summary>The API could not be reached (offline, CORS, DNS). Status 0: there was no HTTP response.</summary>
    public static ApiProblem Unreachable() => new(
        0,
        ClientErrorCodes.Unreachable,
        "The Stockly API cannot be reached.",
        "Check that the API is running and that its CORS settings allow this origin.",
        null,
        NoErrors);

    /// <summary>The API answered with a body this client cannot read.</summary>
    public static ApiProblem InvalidResponse(HttpStatusCode status) => new(
        (int)status,
        ClientErrorCodes.InvalidResponse,
        "Unexpected response from the API.",
        null,
        null,
        NoErrors);

    /// <summary>A failure without a ProblemDetails body (for example a proxy error page).</summary>
    public static ApiProblem FromStatus(HttpStatusCode status, string? traceId) => new(
        (int)status,
        ClientErrorCodes.HttpStatus,
        $"The API answered HTTP {(int)status}.",
        null,
        traceId,
        NoErrors);
}

/// <summary>Codes of failures that happen in the browser, not in the API. They never collide with the API's codes.</summary>
public static class ClientErrorCodes
{
    public const string Unreachable = "Client.Unreachable";
    public const string InvalidResponse = "Client.InvalidResponse";
    public const string HttpStatus = "Client.HttpStatus";
}
