namespace Stockly.Api.Configuration;

/// <summary>Server-wide request limits (docs/security/owasp-api-review.md, API4).</summary>
public static class RequestLimits
{
    /// <summary>
    /// Largest request body Kestrel accepts (1 MiB, Kestrel's default is 30 MB). The largest JSON body, a 100-line I4
    /// review, is far below it; <c>POST /api/invoices/scan</c> raises its own limit with <c>[RequestSizeLimit]</c>.
    /// </summary>
    public const long MaxBodyBytes = 1024 * 1024;
}
