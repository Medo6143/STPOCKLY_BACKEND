using System.Net;
using Stockly.Admin.Auth;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Auth;

public sealed class BearerTokenHandlerTests : IDisposable
{
    private const string OldAccess = "access-old";
    private const string OldRefresh = "refresh-old";
    private const string NewAccess = "access-new";
    private const string NewRefresh = "refresh-new";

    private readonly FakeApi _api = new();
    private readonly AuthSession _session = new();
    private readonly AdminApiClient _client;

    public BearerTokenHandlerTests()
    {
        var authClient = new AdminAuthClient(new HttpClient(_api) { BaseAddress = FakeApi.BaseAddress });
        var handler = new BearerTokenHandler(_session, new TokenRefresher(_session, authClient)) { InnerHandler = _api };
        _client = new AdminApiClient(new HttpClient(handler) { BaseAddress = FakeApi.BaseAddress });
        _session.SignIn(Tokens(OldAccess, OldRefresh));
    }

    [Fact]
    public async Task Send_SignedIn_AttachesTheAccessToken()
    {
        _api.OnGet(ApiRoutes.Plans, Array.Empty<PlanDto>());

        var result = await _client.GetPlansAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _api.To(HttpMethod.Get, ApiRoutes.Plans).Single().BearerToken.Should().Be(OldAccess);
    }

    [Fact]
    public async Task Send_Unauthorized_RefreshesOnceAndRetriesWithTheNewToken()
    {
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Unauthorized, Unauthenticated());
        _api.OnGet(ApiRoutes.Plans, Array.Empty<PlanDto>());
        _api.On(HttpMethod.Post, ApiRoutes.Refresh, HttpStatusCode.OK, Tokens(NewAccess, NewRefresh));

        var result = await _client.GetPlansAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _api.To(HttpMethod.Get, ApiRoutes.Plans).Select(request => request.BearerToken).Should().Equal(OldAccess, NewAccess);
        _api.To(HttpMethod.Post, ApiRoutes.Refresh).Single().Body.Should().Contain(OldRefresh);
        _session.AccessToken.Should().Be(NewAccess);
        _session.RefreshToken.Should().Be(NewRefresh);
    }

    [Fact]
    public async Task Send_ConcurrentUnauthorized_ShareOneRefresh()
    {
        var refreshGate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Unauthorized, Unauthenticated());
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Unauthorized, Unauthenticated());
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Unauthorized, Unauthenticated());
        _api.OnGet(ApiRoutes.Plans, Array.Empty<PlanDto>());
        _api.On(HttpMethod.Post, ApiRoutes.Refresh, () => refreshGate.Task);

        var calls = Enumerable.Range(0, 3).Select(_ => _client.GetPlansAsync(CancellationToken.None)).ToList();
        await WaitUntilAsync(() => _api.To(HttpMethod.Post, ApiRoutes.Refresh).Any() && _api.To(HttpMethod.Get, ApiRoutes.Plans).Count() == 3);
        refreshGate.SetResult(FakeApi.Json(HttpStatusCode.OK, Tokens(NewAccess, NewRefresh)));
        var results = await Task.WhenAll(calls);

        results.Should().OnlyContain(result => result.IsSuccess);
        _api.To(HttpMethod.Post, ApiRoutes.Refresh).Should().ContainSingle();
        _api.To(HttpMethod.Get, ApiRoutes.Plans).Skip(3).Select(request => request.BearerToken).Should().AllBe(NewAccess);
    }

    [Fact]
    public async Task Send_RefreshFails_SignsOutAndReturnsTheUnauthorizedProblem()
    {
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Unauthorized, Unauthenticated());
        _api.Problem(HttpMethod.Post, ApiRoutes.Refresh, HttpStatusCode.Unauthorized, "Auth.RefreshTokenReused", "Sign in again", Samples.TraceId);
        var signedOut = false;
        _session.Changed += (_, _) => signedOut = !_session.IsSignedIn;

        var result = await _client.GetPlansAsync(CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Problem!.Status.Should().Be(401);
        signedOut.Should().BeTrue();
        _session.AccessToken.Should().BeNull();
        _session.RefreshToken.Should().BeNull();
        _api.To(HttpMethod.Get, ApiRoutes.Plans).Should().ContainSingle();
    }

    [Fact]
    public async Task Send_UnauthorizedAgainAfterRefresh_DoesNotRefreshTwice()
    {
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Unauthorized, Unauthenticated());
        _api.On(HttpMethod.Post, ApiRoutes.Refresh, HttpStatusCode.OK, Tokens(NewAccess, NewRefresh));

        var result = await _client.GetPlansAsync(CancellationToken.None);

        result.Problem!.Status.Should().Be(401);
        _api.To(HttpMethod.Post, ApiRoutes.Refresh).Should().ContainSingle();
        _api.To(HttpMethod.Get, ApiRoutes.Plans).Should().HaveCount(2);
    }

    [Fact]
    public async Task Send_SignedOut_SendsNoTokenAndNeverRefreshes()
    {
        _session.SignOut();
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Unauthorized, Unauthenticated());

        var result = await _client.GetPlansAsync(CancellationToken.None);

        result.Problem!.Code.Should().Be("Auth.Unauthenticated");
        _api.To(HttpMethod.Get, ApiRoutes.Plans).Single().BearerToken.Should().BeNull();
        _api.To(HttpMethod.Post, ApiRoutes.Refresh).Should().BeEmpty();
    }

    private static AdminAuthResponse Tokens(string access, string refresh) =>
        new(access, FixedClock.Now.UtcDateTime, refresh, FixedClock.Now.UtcDateTime, new AdminUserDto(Guid.NewGuid(), "admin@example.com", "Admin"));

    private static object Unauthenticated() => new { title = "Unauthorized", status = 401, code = "Auth.Unauthenticated" };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public void Dispose() => _api.Dispose();
}
