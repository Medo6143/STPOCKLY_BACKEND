namespace Stockly.Admin.Auth;

/// <summary>
/// Refreshes the session with AD2, single-flight (contract §1.4 client rule): requests that get <c>401</c> at the same time
/// share one refresh. A request whose token was already replaced gets the new token without another refresh. When the
/// refresh fails the session is signed out, and the app returns to the login page.
/// </summary>
public sealed class TokenRefresher(AuthSession session, AdminAuthClient authClient)
{
    private readonly Lock _gate = new();
    private Task<string?>? _inFlight;

    /// <summary>
    /// Returns a fresh access token to retry with, or <c>null</c> when the admin must sign in again.
    /// <paramref name="rejectedAccessToken"/> is the token the API just rejected.
    /// </summary>
    public Task<string?> RefreshAsync(string rejectedAccessToken)
    {
        lock (_gate)
        {
            var current = session.AccessToken;
            if (current is null)
            {
                return Task.FromResult<string?>(null);
            }

            if (!string.Equals(current, rejectedAccessToken, StringComparison.Ordinal))
            {
                return Task.FromResult<string?>(current);
            }

            if (_inFlight is null || _inFlight.IsCompleted)
            {
                _inFlight = RunAsync();
            }

            return _inFlight;
        }
    }

    // Not cancellable by one caller: other requests may be waiting for the same refresh.
    private async Task<string?> RunAsync()
    {
        var refreshToken = session.RefreshToken;
        if (refreshToken is null)
        {
            session.SignOut();
            return null;
        }

        var result = await authClient.RefreshAsync(refreshToken, CancellationToken.None);
        if (!result.IsSuccess)
        {
            session.SignOut();
            return null;
        }

        session.SignIn(result.Value);
        return result.Value.AccessToken;
    }
}
