using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Stockly.Admin.Auth;
using Stockly.Admin.Configuration;
using Stockly.Admin.Services;
using Stockly.Admin.Theme;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Support;

/// <summary>
/// bUnit context with MudBlazor, a signed-in admin session and the real typed clients talking to <see cref="FakeApi"/>.
/// The popover and dialog providers are rendered so selects, menus and dialogs work like in the app.
/// </summary>
public abstract class AdminTestContext : BunitContext
{
    public const string AccessToken = "access-1";
    public const string RefreshToken = "refresh-1";

    protected AdminTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(AdminOptions.From(FakeApi.BaseAddress.AbsoluteUri));
        Services.AddSingleton<TimeProvider>(new FixedClock());
        Services.AddSingleton<ThemeState>();
        Services.AddSingleton(Session);
        Services.AddSingleton(new AdminAuthClient(new HttpClient(Api) { BaseAddress = FakeApi.BaseAddress }));
        Services.AddSingleton<TokenRefresher>();
        Services.AddSingleton(provider => new AdminApiClient(new HttpClient(
            new BearerTokenHandler(Session, provider.GetRequiredService<TokenRefresher>()) { InnerHandler = Api })
        {
            BaseAddress = FakeApi.BaseAddress,
        }));
        Services.AddSingleton<AccountService>();

        Session.SignIn(new AdminAuthResponse(
            AccessToken, FixedClock.Now.UtcDateTime.AddMinutes(15), RefreshToken, FixedClock.Now.UtcDateTime.AddDays(30),
            new AdminUserDto(Guid.NewGuid(), "admin@example.com", "Platform Admin")));

        Render<MudPopoverProvider>();
        Dialogs = Render<MudDialogProvider>();
    }

    protected FakeApi Api { get; } = new();

    protected AuthSession Session { get; } = new();

    protected IRenderedComponent<MudDialogProvider> Dialogs { get; }

    // xUnit v2 disposes test classes synchronously; MudBlazor registers services that only support async disposal.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        base.Dispose(disposing);
    }
}
