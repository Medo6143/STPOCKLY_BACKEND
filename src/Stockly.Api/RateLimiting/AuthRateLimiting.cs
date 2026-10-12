using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Stockly.Api.Errors;
using Stockly.Application.Features.Auth;

namespace Stockly.Api.RateLimiting;

/// <summary>
/// Built-in ASP.NET Core rate limiter for the anonymous auth endpoints (contract §1.4 and decision 19): one fixed-window
/// policy per endpoint, partitioned by client IP. A rejection is <c>429 Auth.TooManyRequests</c> with <c>Retry-After</c>.
/// </summary>
public static class AuthRateLimiting
{
    public const string LoginPolicy = "auth-login";
    public const string RegisterPolicy = "auth-register";

    /// <summary>Partition for requests without a remote address (e.g. in-memory hosts); they share one limit.</summary>
    public const string UnknownClientPartition = "unknown-client";

    private const string VariablePrefix = "RateLimiting__Auth__";

    public static IServiceCollection AddStocklyRateLimiting(this IServiceCollection services, AuthRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, cancellationToken) => RejectAsync(context, options.Window, cancellationToken);
            limiter.AddPolicy(LoginPolicy, context => PerClientIp(context, options));
            limiter.AddPolicy(RegisterPolicy, context => PerClientIp(context, options));
        });
        return services;
    }

    /// <summary>Reads <see cref="AuthRateLimitOptions"/>; fails startup naming the variable when a value is not a positive integer.</summary>
    public static AuthRateLimitOptions GetAuthRateLimitOptions(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(AuthRateLimitOptions.SectionName);
        return new AuthRateLimitOptions
        {
            PermitLimit = PositiveOr(section, nameof(AuthRateLimitOptions.PermitLimit), AuthRateLimitOptions.DefaultPermitLimit),
            WindowSeconds = PositiveOr(section, nameof(AuthRateLimitOptions.WindowSeconds), AuthRateLimitOptions.DefaultWindowSeconds),
        };
    }

    public static string PartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.RemoteIpAddress?.ToString() ?? UnknownClientPartition;
    }

    private static RateLimitPartition<string> PerClientIp(HttpContext context, AuthRateLimitOptions options) =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = options.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    // A framework callback outside MVC: the problem is built like every other one (StocklyProblems) and written directly.
    private static async ValueTask RejectAsync(OnRejectedContext context, TimeSpan window, CancellationToken cancellationToken)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var fromLease) ? fromLease : window;
        var http = context.HttpContext;
        var problem = StocklyProblems.FromError(http, AuthErrors.TooManyRequests with { RetryAfter = retryAfter });
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await http.Response.WriteAsJsonAsync(problem, options: null, StocklyProblems.ContentType, cancellationToken);
    }

    private static int PositiveOr(IConfigurationSection section, string key, int fallback)
    {
        var text = section[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"{VariablePrefix}{key} must be a positive whole number.");
    }
}
