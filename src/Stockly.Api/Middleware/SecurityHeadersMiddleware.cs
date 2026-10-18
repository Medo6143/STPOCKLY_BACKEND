namespace Stockly.Api.Middleware;

/// <summary>
/// Response hardening headers on every response, errors included (set when the response starts, so the exception
/// handler's reset does not drop them): no MIME sniffing, no framing, no referrer, no powerful browser features, and a
/// deny-all Content-Security-Policy for the JSON API (docs/security/owasp-api-review.md, API8). The Hangfire dashboard
/// (<c>/hangfire</c>) serves its own HTML, scripts and styles, so it gets the same headers without that CSP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    private const string HangfirePath = "/hangfire";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isDashboard = context.Request.Path.StartsWithSegments(HangfirePath, StringComparison.OrdinalIgnoreCase);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (!isDashboard)
            {
                headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
