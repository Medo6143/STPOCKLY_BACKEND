namespace Stockly.Api.Configuration;

/// <summary>
/// CORS for <c>Stockly.Admin</c> (Blazor WebAssembly, ADR-0005), which calls the API from its own origin. The allowed
/// origins come from configuration only (<c>Cors__AdminOrigins</c>, comma-separated, e.g. <c>http://127.0.0.1:5080</c>).
/// Without it no cross-origin request is allowed. No credentials: the admin sends a bearer token, never cookies.
/// </summary>
public static class AdminCors
{
    public const string PolicyName = "admin";
    public const string OriginsKey = "Cors:AdminOrigins";

    private const string OriginsVariable = "Cors__AdminOrigins";
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string RetryAfterHeader = "Retry-After";
    private const string RootPath = "/";

    private static readonly string[] Methods = ["GET", "POST", "PUT"];
    private static readonly string[] RequestHeaders = ["Authorization", "Content-Type", "Accept", "Accept-Language", CorrelationIdHeader];
    private static readonly string[] ExposedHeaders = [CorrelationIdHeader, RetryAfterHeader];

    public static IServiceCollection AddAdminCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var origins = ReadOrigins(configuration);
        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Count > 0)
            {
                policy.WithOrigins([.. origins])
                    .WithMethods(Methods)
                    .WithHeaders(RequestHeaders)
                    .WithExposedHeaders(ExposedHeaders);
            }
        }));
        return services;
    }

    /// <summary>Absolute http(s) origins; fails start-up naming the variable when one is not.</summary>
    public static IReadOnlyList<string> ReadOrigins(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var origins = (configuration[OriginsKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.AbsolutePath != RootPath)
            {
                throw new InvalidOperationException(
                    $"{OriginsVariable} must be a comma-separated list of origins such as http://127.0.0.1:5080 (no path).");
            }
        }

        return origins.Select(origin => origin.TrimEnd(RootPath[0])).ToList();
    }
}
