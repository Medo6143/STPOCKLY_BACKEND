using Stockly.Contracts.Admin;

namespace Stockly.Admin.Auth;

/// <summary>
/// The signed-in admin's tokens, kept in memory only (never in <c>localStorage</c> or <c>sessionStorage</c>, so a script
/// injected into the page cannot read them from storage, and nothing survives the tab). Reloading the page signs the admin
/// out. One instance per browser tab (singleton). Tokens are never logged or shown.
/// </summary>
public sealed class AuthSession
{
    /// <summary>Raised after every sign-in, token rotation and sign-out.</summary>
    public event EventHandler? Changed;

    public string? AccessToken { get; private set; }

    public AdminUserDto? User { get; private set; }

    public bool IsSignedIn => AccessToken is not null;

    /// <summary>The refresh token, read only by the refresh and the logout.</summary>
    public string? RefreshToken { get; private set; }

    /// <summary>Stores the tokens of AD1 or AD2 (a rotation replaces both).</summary>
    public void SignIn(AdminAuthResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        AccessToken = response.AccessToken;
        RefreshToken = response.RefreshToken;
        User = response.User;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets the tokens. Does nothing when already signed out.</summary>
    public void SignOut()
    {
        if (AccessToken is null && RefreshToken is null)
        {
            return;
        }

        AccessToken = null;
        RefreshToken = null;
        User = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
