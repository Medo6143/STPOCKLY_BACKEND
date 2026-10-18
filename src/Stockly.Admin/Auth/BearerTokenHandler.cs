using System.Net;
using System.Net.Http.Headers;

namespace Stockly.Admin.Auth;

/// <summary>
/// Adds <c>Authorization: Bearer</c> from the in-memory session. On <c>401</c> it refreshes once (shared with concurrent
/// requests, see <see cref="TokenRefresher"/>) and retries the request once; a second <c>401</c> is returned as is.
/// The token is never logged.
/// </summary>
public sealed class BearerTokenHandler(AuthSession session, TokenRefresher refresher) : DelegatingHandler
{
    private const string Scheme = "Bearer";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = session.AccessToken;
        if (token is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(Scheme, token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var freshToken = await refresher.RefreshAsync(token);
        if (freshToken is null)
        {
            return response;
        }

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue(Scheme, freshToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
