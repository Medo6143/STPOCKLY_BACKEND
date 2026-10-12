using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Infrastructure.Identity;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Persistence.Interceptors;
using Stockly.Infrastructure.Persistence.Repositories;
using Stockly.Infrastructure.Time;

namespace Stockly.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="StocklyDbContext"/> with its interceptors, <see cref="IClock"/>, the repositories and the
    /// unit of work. The caller registers <c>ICurrentTenant</c> (from the JWT in the Api).
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

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IShopRepository, ShopRepository>();
        services.AddScoped<IShopMembershipRepository, ShopMembershipRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IShopSubscriptionRepository, ShopSubscriptionRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity on <see cref="StocklyDbContext"/> (users only, no roles), the password policy, and the
    /// token issuer. Requires <see cref="AddPersistence"/> and logging.
    /// </summary>
    public static IServiceCollection AddStocklyIdentity(this IServiceCollection services, JwtOptions jwtOptions)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);

        services.AddSingleton(jwtOptions);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = AuthLimits.PasswordMinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = true;

                // The user name is the email; any character an email may hold is allowed.
                options.User.AllowedUserNameCharacters = string.Empty;
            })
            .AddEntityFrameworkStores<StocklyDbContext>();
        services.AddScoped<IUserAccounts, IdentityUserAccounts>();

        return services;
    }
}
