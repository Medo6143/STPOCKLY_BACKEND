using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stockly.Application.Abstractions.Time;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Persistence.Interceptors;
using Stockly.Infrastructure.Time;

namespace Stockly.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="StocklyDbContext"/> with its interceptors and <see cref="IClock"/>.
    /// The caller registers <c>ICurrentTenant</c> (from the JWT in the Api).
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<TenantStampInterceptor>();
        services.AddScoped<AuditTimestampsInterceptor>();
        services.AddDbContext<StocklyDbContext>((provider, options) =>
            StocklyDbContextOptions.Configure(options, connectionString)
                .AddInterceptors(
                    provider.GetRequiredService<TenantStampInterceptor>(),
                    provider.GetRequiredService<AuditTimestampsInterceptor>()));

        return services;
    }
}
