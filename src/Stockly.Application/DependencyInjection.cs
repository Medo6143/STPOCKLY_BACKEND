using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stockly.Application.Behaviors;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Application.Features.Invoices.Validation;
using Stockly.Application.Features.Products;

namespace Stockly.Application;

public static class DependencyInjection
{
    /// <summary>
    /// MediatR handlers with the pipeline Validation → catalog cache invalidation (catalog writes only, after commit) →
    /// Transaction (commands only) → handler, FluentValidation validators, the auth session issuer and the catalog list
    /// cache, the invoice warning rules and the product match strategies (one class each, SOLID O). Infrastructure
    /// registers the interfaces these depend on.
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
        services.AddScoped<AdminSessions>();
        services.AddScoped<AdminAudit>();
        services.AddScoped<CatalogListCache>();
        services.AddScoped<InvoiceValidator>();
        services.AddScoped<IInvoiceValidationRule, ItemCapExceededRule>();
        services.AddScoped<IInvoiceValidationRule, UnreadableLinesRule>();
        services.AddScoped<IInvoiceValidationRule, TotalMismatchRule>();
        services.AddScoped<IInvoiceValidationRule, DuplicateInvoiceNumberRule>();
        services.AddScoped<IProductMatcher, ProductMatcher>();
        services.AddScoped<IProductMatchStrategy, ExactAliasMatchStrategy>();
        services.AddScoped<IProductMatchStrategy, ExactNameMatchStrategy>();
        services.AddScoped<IProductMatchStrategy, TrigramMatchStrategy>();

        // The default threshold unless the host binds section Matching (Infrastructure's AddInvoiceScanning does).
        services.TryAddSingleton(new MatchingOptions());

        return services;
    }
}
