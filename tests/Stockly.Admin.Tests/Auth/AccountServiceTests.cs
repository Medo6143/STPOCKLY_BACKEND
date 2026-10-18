using System.Net;
using Stockly.Admin.Auth;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Auth;

public sealed class AccountServiceTests : IDisposable
{
    private readonly FakeApi _api = new();
    private readonly AuthSession _session = new();
    private readonly AccountService _account;

    public AccountServiceTests()
    {
        var authClient = new AdminAuthClient(new HttpClient(_api) { BaseAddress = FakeApi.BaseAddress });
        var handler = new BearerTokenHandler(_session, new TokenRefresher(_session, authClient)) { InnerHandler = _api };
        _account = new AccountService(_session, authClient, new AdminApiClient(new HttpClient(handler) { BaseAddress = FakeApi.BaseAddress }));
        _session.SignIn(new AdminAuthResponse("access-1", DateTime.UtcNow, "refresh-1", DateTime.UtcNow, new AdminUserDto(Guid.NewGuid(), "a@example.com", "A")));
    }

    [Fact]
    public async Task SignOut_RevokesTheRefreshTokenThenForgetsTheSession()
    {
        _api.On(HttpMethod.Post, ApiRoutes.Logout, HttpStatusCode.NoContent);

        await _account.SignOutAsync(CancellationToken.None);

        var logout = _api.To(HttpMethod.Post, ApiRoutes.Logout).Single();
        logout.BearerToken.Should().Be("access-1");
        logout.Body.Should().Be("{\"refreshToken\":\"refresh-1\"}");
        _session.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task SignOut_ApiUnreachable_StillSignsOutLocally()
    {
        _api.On(HttpMethod.Post, ApiRoutes.Logout, () => throw new HttpRequestException("offline"));

        await _account.SignOutAsync(CancellationToken.None);

        _session.IsSignedIn.Should().BeFalse();
    }

    public void Dispose() => _api.Dispose();
}
