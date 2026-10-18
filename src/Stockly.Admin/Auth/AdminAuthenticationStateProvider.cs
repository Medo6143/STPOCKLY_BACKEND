using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Stockly.Admin.Auth;

/// <summary>
/// Exposes <see cref="AuthSession"/> to Blazor's <c>[Authorize]</c> and <c>AuthorizeView</c>. The principal carries the
/// admin's id, name and email; the token itself never leaves the session. Every session change is announced.
/// </summary>
public sealed class AdminAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    public const string AuthenticationType = "StocklyAdmin";

    private readonly AuthSession _session;

    public AdminAuthenticationStateProvider(AuthSession session)
    {
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(CurrentState());

    public void Dispose() => _session.Changed -= OnSessionChanged;

    private void OnSessionChanged(object? sender, EventArgs e) => NotifyAuthenticationStateChanged(Task.FromResult(CurrentState()));

    private AuthenticationState CurrentState()
    {
        if (!_session.IsSignedIn || _session.User is not { } user)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
        ];
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType)));
    }
}
