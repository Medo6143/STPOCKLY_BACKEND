using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Services;
using Stockly.Admin.Auth;
using Stockly.Admin.Configuration;
using Stockly.Admin.Services;
using Stockly.Admin.Theme;

namespace Stockly.Admin;

/// <summary>The admin app's composition root (constructor injection only, AGENTS.md rule 42).</summary>
public static class AdminServiceCollectionExtensions
{
    /// <summary>The admin UI is English (brief 5b); the API returns English ProblemDetails text and plan names.</summary>
    public const string UiLanguage = "en";

    public static IServiceCollection AddStocklyAdmin(this IServiceCollection services, AdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ThemeState>();

        // Tokens live in memory for the life of the tab (see AuthSession).
        services.AddSingleton<AuthSession>();
        services.AddSingleton<TokenRefresher>();
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient<AdminAuthClient>(client => ConfigureApi(client, options));
        services.AddHttpClient<AdminApiClient>(client => ConfigureApi(client, options))
            .AddHttpMessageHandler<BearerTokenHandler>();
        services.AddScoped<AccountService>();

        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<AdminAuthenticationStateProvider>();
        services.AddSingleton<AuthenticationStateProvider>(provider => provider.GetRequiredService<AdminAuthenticationStateProvider>());

        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.ShowCloseIcon = true;
            config.SnackbarConfiguration.VisibleStateDuration = 5000;
        });
        return services;
    }

    private static void ConfigureApi(HttpClient client, AdminOptions options)
    {
        client.BaseAddress = options.ApiBaseUrl;
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(UiLanguage));
    }
}
