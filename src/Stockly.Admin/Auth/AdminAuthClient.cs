using Stockly.Admin.Services;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Auth;

namespace Stockly.Admin.Auth;

/// <summary>
/// Typed client of the anonymous auth endpoints AD1 (login) and AD2 (refresh). Its <see cref="HttpClient"/> has no bearer
/// handler, so a refresh can never trigger another refresh.
/// </summary>
public sealed class AdminAuthClient(HttpClient http)
{
    public Task<ApiResult<AdminAuthResponse>> LoginAsync(AdminLoginRequest request, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<AdminAuthResponse>(http, ApiRoutes.Login, request, cancellationToken);

    public Task<ApiResult<AdminAuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<AdminAuthResponse>(http, ApiRoutes.Refresh, new RefreshTokenRequest(refreshToken), cancellationToken);
}
