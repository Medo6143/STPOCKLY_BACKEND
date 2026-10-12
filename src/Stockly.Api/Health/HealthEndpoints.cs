using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Stockly.Api.Health;

/// <summary>
/// <c>/health/live</c>: the process answers (no dependencies). <c>/health/ready</c>: PostgreSQL (down → 503) and
/// Redis (down → Degraded, still 200: Redis failure never fails a request, docs/design/redis.md §4).
/// Bodies list status and duration per check, never exception messages.
/// </summary>
public static class HealthEndpoints
{
    public const string Live = "/health/live";
    public const string Ready = "/health/ready";

    private const string ReadyTag = "ready";
    private static readonly TimeSpan RedisTimeout = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddStocklyHealthChecks(this IServiceCollection services, string postgres, string redis)
    {
        services.AddHealthChecks()
            .AddNpgSql(postgres, name: "postgres", tags: [ReadyTag])
            .AddRedis(redis, name: "redis", failureStatus: HealthStatus.Degraded, tags: [ReadyTag], timeout: RedisTimeout);
        return services;
    }

    public static IEndpointRouteBuilder MapStocklyHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(Live, new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync });
        endpoints.MapHealthChecks(Ready, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteAsync,
        });
        return endpoints;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var body = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
            }),
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Json), context.RequestAborted);
    }
}
