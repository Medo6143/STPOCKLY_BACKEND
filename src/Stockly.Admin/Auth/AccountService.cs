using Stockly.Admin.Services;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Auth;

/// <summary>Sign-in (AD1) and sign-out (AD3) for the pages; the session holds the result.</summary>
public sealed class AccountService(AuthSession session, AdminAuthClient authClient, AdminApiClient api)
{
    public async Task<ApiProblem?> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var result = await authClient.LoginAsync(new AdminLoginRequest(email.Trim(), password), cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Problem;
        }

        session.SignIn(result.Value);
        return null;
    }

    /// <summary>
    /// Revokes the refresh token family on the server (AD3), then forgets the tokens. The local sign-out happens even when
    /// the server cannot be reached; the token then simply expires.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        if (session.RefreshToken is { } refreshToken)
        {
            await api.LogoutAsync(refreshToken, cancellationToken);
        }

        session.SignOut();
    }
}
