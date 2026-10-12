using System.Globalization;
using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Auth;

/// <summary>
/// A1/A2 rate limiting (contract §1.4, v1.4) with the Api's default limits: 10 requests per minute per client IP and
/// endpoint; the 11th is <c>429 Auth.TooManyRequests</c> with <c>Retry-After</c>.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class RateLimitEndpointTests(ApiFixture fixture)
{
    private const int DefaultPermitLimit = 10;
    private const int DefaultWindowSeconds = 60;
    private const string ClientA = "203.0.113.10";
    private const string ClientB = "203.0.113.20";

    [Fact]
    public async Task Login_EleventhRequestFromOneIpInAMinute_Returns429WithRetryAfter_OtherIpNotThrottled()
    {
        await using var factory = fixture.CreateFactory(authPermitLimit: null);
        using var client = factory.CreateClient();

        for (var request = 1; request <= DefaultPermitLimit; request++)
        {
            var processed = await LoginFromAsync(client, ClientA);
            processed.Code.Should().Be("Auth.InvalidCredentials", "request {0} is within the limit", request);
        }

        var throttled = await LoginFromAsync(client, ClientA);
        var otherIp = await LoginFromAsync(client, ClientB);

        AssertTooManyRequests(throttled);
        otherIp.Status.Should().Be(HttpStatusCode.Unauthorized, otherIp.Raw);
    }

    [Fact]
    public async Task Register_EleventhRequestFromOneIpInAMinute_Returns429WithRetryAfter_OtherIpNotThrottled()
    {
        await using var factory = fixture.CreateFactory(authPermitLimit: null);
        using var client = factory.CreateClient();

        for (var request = 1; request <= DefaultPermitLimit; request++)
        {
            var processed = await RegisterFromAsync(client, ClientA);
            processed.Status.Should().Be(HttpStatusCode.Created, "request {0} is within the limit: {1}", request, processed.Raw);
        }

        var throttled = await RegisterFromAsync(client, ClientA);
        var otherIp = await RegisterFromAsync(client, ClientB);

        AssertTooManyRequests(throttled);
        otherIp.Status.Should().Be(HttpStatusCode.Created, otherIp.Raw);
    }

    [Fact]
    public async Task LoginAndRegister_HaveSeparateLimits()
    {
        await using var factory = fixture.CreateFactory(authPermitLimit: null);
        using var client = factory.CreateClient();
        for (var request = 0; request <= DefaultPermitLimit; request++)
        {
            await LoginFromAsync(client, ClientA);
        }

        var register = await RegisterFromAsync(client, ClientA);

        register.Status.Should().Be(HttpStatusCode.Created, register.Raw);
    }

    [Fact]
    public async Task Throttled_InArabicAndEnglish_SameCodeTranslatedDetail()
    {
        await using var factory = fixture.CreateFactory(authPermitLimit: null);
        using var client = factory.CreateClient();
        for (var request = 0; request < DefaultPermitLimit; request++)
        {
            await LoginFromAsync(client, ClientA);
        }

        var arabic = await LoginFromAsync(client, ClientA, "ar");
        var english = await LoginFromAsync(client, ClientA, "en");

        arabic.Code.Should().Be("Auth.TooManyRequests");
        english.Code.Should().Be("Auth.TooManyRequests");
        arabic.Body!["detail"]!.GetValue<string>().Should().NotBe(english.Body!["detail"]!.GetValue<string>());
    }

    private static void AssertTooManyRequests(ApiResponse response)
    {
        response.Status.Should().Be(HttpStatusCode.TooManyRequests, response.Raw);
        response.Code.Should().Be("Auth.TooManyRequests");
        response.ContentType.Should().Be("application/problem+json");
        response.Body!["traceId"].Should().NotBeNull();
        int.Parse(response.RetryAfter!, CultureInfo.InvariantCulture).Should().BeInRange(1, DefaultWindowSeconds);
    }

    private static Task<ApiResponse> LoginFromAsync(HttpClient client, string clientIp, string? language = null) =>
        AuthApi.PostFromAsync(client, clientIp, AuthApi.Login, new { email = AuthApi.NewEmail(), password = AuthApi.Password }, language);

    private static Task<ApiResponse> RegisterFromAsync(HttpClient client, string clientIp) =>
        AuthApi.PostFromAsync(client, clientIp, AuthApi.Register, AuthApi.RegisterBody(AuthApi.NewEmail()));
}
