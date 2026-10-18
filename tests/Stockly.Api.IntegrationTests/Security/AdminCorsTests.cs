using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Security;

/// <summary>
/// CORS for Stockly.Admin from <c>Cors__AdminOrigins</c> only (ADR-0005, docs/security/owasp-api-review.md API8): the
/// configured origin gets the CORS headers, another origin does not, and with nothing configured no origin does.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AdminCorsTests(ApiFixture fixture)
{
    private const string AdminOrigin = "http://127.0.0.1:5080";
    private const string OtherOrigin = "https://evil.example";
    private const string AllowOrigin = "Access-Control-Allow-Origin";

    [Fact]
    public async Task Preflight_ConfiguredOrigin_AllowedWithTheAdminMethodsAndHeaders()
    {
        await using var factory = Factory(AdminOrigin + ", http://localhost:5080/");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Preflight(AdminOrigin, "PUT", "authorization,content-type"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Header(response, AllowOrigin).Should().Be(AdminOrigin);
        Header(response, "Access-Control-Allow-Methods").Should().Contain("PUT");
        Header(response, "Access-Control-Allow-Headers").Should().ContainEquivalentOf("authorization");
        Header(response, "Access-Control-Allow-Credentials").Should().BeNull("the admin sends a bearer token, never cookies");
    }

    [Fact]
    public async Task Request_ConfiguredOrigin_GetsAllowOriginAndExposedHeaders()
    {
        await using var factory = Factory(AdminOrigin);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AuthApi.Me, UriKind.Relative));
        request.Headers.Add("Origin", AdminOrigin);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "CORS runs before authentication; the error still carries the headers");
        Header(response, AllowOrigin).Should().Be(AdminOrigin);
        Header(response, "Access-Control-Expose-Headers").Should().Contain("X-Correlation-Id").And.Contain("Retry-After");
    }

    [Fact]
    public async Task Preflight_SecondConfiguredOrigin_TrailingSlashTrimmed_Allowed()
    {
        await using var factory = Factory(AdminOrigin + ",http://localhost:5080/");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Preflight("http://localhost:5080", "GET", "authorization"));

        Header(response, AllowOrigin).Should().Be("http://localhost:5080");
    }

    [Theory]
    [InlineData(OtherOrigin)]
    [InlineData("http://127.0.0.1:5081")]
    [InlineData("https://127.0.0.1:5080")]
    public async Task Preflight_OtherOrigin_NoCorsHeaders(string origin)
    {
        await using var factory = Factory(AdminOrigin);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Preflight(origin, "GET", "authorization"));

        Header(response, AllowOrigin).Should().BeNull();
        Header(response, "Access-Control-Allow-Methods").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Preflight_NoOriginConfigured_NoOriginAllowed(string? origins)
    {
        await using var factory = Factory(origins);
        using var client = factory.CreateClient();

        using var preflight = await client.SendAsync(Preflight(AdminOrigin, "GET", "authorization"));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AuthApi.Me, UriKind.Relative));
        request.Headers.Add("Origin", AdminOrigin);
        using var response = await client.SendAsync(request);

        Header(preflight, AllowOrigin).Should().BeNull();
        Header(response, AllowOrigin).Should().BeNull();
    }

    [Theory]
    [InlineData("127.0.0.1:5080")]
    [InlineData("http://127.0.0.1:5080/admin")]
    [InlineData("ftp://127.0.0.1")]
    public async Task Startup_InvalidOrigin_FailsWithVariableName(string origins)
    {
        await using var factory = Factory(origins);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cors__AdminOrigins*");
    }

    private StocklyApiFactory Factory(string? origins) =>
        fixture.CreateFactory(settings: new Dictionary<string, string?> { ["Cors:AdminOrigins"] = origins });

    private static HttpRequestMessage Preflight(string origin, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, new Uri(AuthApi.Me, UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
