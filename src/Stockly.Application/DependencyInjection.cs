using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Behaviors;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Products;

namespace Stockly.Application;

public static class DependencyInjection
{
    /// <summary>
    /// MediatR handlers with the pipeline Validation → catalog cache invalidation (catalog writes only, after commit) →
    /// Transaction (commands only) → handler, FluentValidation validators, the auth session issuer and the catalog list
    /// cache. Infrastructure registers the interfaces these depend on.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(CatalogCacheInvalidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<AuthSessions>();
        services.AddScoped<CatalogListCache>();

        return services;
    }
}
