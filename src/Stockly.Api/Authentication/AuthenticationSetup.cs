using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Stockly.Api.Errors;
using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Identity;

namespace Stockly.Api.Authentication;

public static class AuthenticationSetup
{
    /// <summary>JWT bearer authentication (claims kept as issued: <c>sub</c>, <c>shop_id</c>, <c>role</c>) and the policies.</summary>
    public static IServiceCollection AddStocklyAuthentication(this IServiceCollection services, JwtOptions jwtOptions)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);

        services.AddScoped<ProblemWriter>();
        services.AddScoped<ProblemJwtBearerEvents>();
        services.AddScoped<IAuthorizationHandler, ActiveShopHandler>();
        services.AddScoped<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = jwtOptions.CreateValidationParameters();
                options.EventsType = typeof(ProblemJwtBearerEvents);
            });

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(ActiveShopPolicy().Build())
            .AddPolicy(AuthPolicies.Owner, ActiveShopPolicy().RequireRole(nameof(ShopRole.Owner)).Build())
            .AddPolicy(AuthPolicies.AllowSuspendedShop, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(StocklyClaimTypes.ShopId))
            .AddPolicy(AuthPolicies.PlatformAdmin, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(StocklyClaimTypes.PlatformRole, StocklyClaimTypes.PlatformAdminValue)
                .RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == StocklyClaimTypes.ShopId)));

        return services;
    }

    private static AuthorizationPolicyBuilder ActiveShopPolicy() =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveShopRequirement());
}
