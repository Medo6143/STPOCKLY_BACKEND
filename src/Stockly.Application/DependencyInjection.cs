using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Behaviors;
using Stockly.Application.Features.Auth;

namespace Stockly.Application;

public static class DependencyInjection
{
    /// <summary>
    /// MediatR handlers with the pipeline Validation → Transaction (commands only) → handler, FluentValidation
    /// validators, and the auth session issuer. Infrastructure registers the interfaces these depend on.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<AuthSessions>();

        return services;
    }
}
