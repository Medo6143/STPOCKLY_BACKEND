using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stockly.Api.Configuration;
using Stockly.Api.IntegrationTests.Support;
using Stockly.Api.Middleware;

namespace Stockly.Api.IntegrationTests.Security;

/// <summary>The fixes of docs/security/owasp-api-review.md: response headers, request limits, forwarded headers, Production errors.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class SecurityHardeningTests(ApiFixture fixture)
{
    private const string Connection = "/__tests/connection";

    [Theory]
    [InlineData("/__tests/connection", HttpStatusCode.OK)]
    [InlineData("/this-route-does-not-exist", HttpStatusCode.NotFound)]
    [InlineData("/__tests/throw", HttpStatusCode.InternalServerError)]
    [InlineData("/api/auth/me", HttpStatusCode.Unauthorized)]
    public async Task Responses_CarryTheSecurityHeaders_ErrorsIncluded(string path, HttpStatusCode status)
    {
        await using var factory = fixture.CreateFactory(includeTestEndpoints: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(status);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Permissions-Policy").Should().Contain("camera=()");
        Header(response, "Content-Security-Policy").Should().Be(SecurityHeadersMiddleware.ApiContentSecurityPolicy);
    }

    [Fact]
    public async Task HangfireDashboard_SecurityHeadersWithoutTheApiCsp()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/hangfire", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Content-Security-Policy").Should().BeNull("the dashboard serves its own scripts and styles");
    }

    [Fact]
    public async Task Kestrel_NoServerHeaderAndOneMebibyteBodyLimit()
    {
        await using var factory = fixture.CreateFactory();

        var kestrel = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        kestrel.AddServerHeader.Should().BeFalse();
        kestrel.Limits.MaxRequestBodySize.Should().Be(RequestLimits.MaxBodyBytes).And.Be(1024 * 1024);
    }

    [Fact]
    public async Task Production_UnexpectedError_NoExceptionDetailsAndNoOpenApi()
    {
        await using var factory = fixture.CreateFactory(includeTestEndpoints: true, environment: "Production");
        using var client = factory.CreateClient();

        using var error = await client.GetAsync(new Uri("/__tests/throw", UriKind.Relative));
        var body = await error.Content.ReadAsStringAsync();
        using var openApi = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        error.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = JsonNode.Parse(body)!;
        problem["code"]!.GetValue<string>().Should().Be("Server.Unexpected");
        problem["traceId"].Should().NotBeNull();
        body.Should().NotContain(TestOnlyController.SecretDetail)
            .And.NotContain("InvalidOperationException")
            .And.NotContain(" at ")
            .And.NotContain("stackTrace", "ProblemDetails never carry exception details");
        openApi.StatusCode.Should().Be(HttpStatusCode.NotFound, "the OpenAPI document is not served in Production");
    }

    [Fact]
    public async Task ForwardedHeaders_Off_TheProxyAddressIsTheClient()
    {
        await using var factory = fixture.CreateFactory(includeTestEndpoints: true);
        using var client = factory.CreateClient();

        var seen = await ConnectionAsync(client, peer: "10.0.0.1", forwardedFor: "203.0.113.7");

        seen.Should().Be(("10.0.0.1", "http"), "forwarded headers are ignored unless enabled");
    }

    [Fact]
    public async Task ForwardedHeaders_OnFromAKnownProxy_ClientIpAndSchemeFromTheHeaders()
    {
        await using var factory = ForwardingFactory("10.0.0.1", networks: null);
        using var client = factory.CreateClient();

        (await ConnectionAsync(client, peer: "10.0.0.1", forwardedFor: "203.0.113.7")).Should().Be(("203.0.113.7", "https"));
        (await ConnectionAsync(client, peer: "10.0.0.2", forwardedFor: "203.0.113.7"))
            .Should().Be(("10.0.0.2", "http"), "an unknown proxy is not trusted");
    }

    [Fact]
    public async Task ForwardedHeaders_OnFromAKnownNetwork_OneHopOnly()
    {
        await using var factory = ForwardingFactory(proxies: null, "10.1.0.0/16");
        using var client = factory.CreateClient();

        (await ConnectionAsync(client, peer: "10.1.2.3", forwardedFor: "198.51.100.1, 203.0.113.7"))
            .Should().Be(("203.0.113.7", "https"), "only the last hop, added by the trusted proxy, is used");
    }

    [Theory]
    [InlineData("Enabled", "yes", "ForwardedHeaders__Enabled")]
    [InlineData("KnownProxies", "not-an-ip", "ForwardedHeaders__KnownProxies")]
    [InlineData("KnownNetworks", "10.0.0.0/99", "ForwardedHeaders__KnownNetworks")]
    public async Task ForwardedHeaders_InvalidSetting_StartupFailsWithVariableName(string key, string value, string variable)
    {
        var settings = new Dictionary<string, string?> { ["ForwardedHeaders:Enabled"] = "true", [$"ForwardedHeaders:{key}"] = value };
        await using var factory = fixture.CreateFactory(settings: settings);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{variable}*");
    }

    private StocklyApiFactory ForwardingFactory(string? proxies, string? networks) =>
        fixture.CreateFactory(
            includeTestEndpoints: true,
            settings: new Dictionary<string, string?>
            {
                ["ForwardedHeaders:Enabled"] = "true",
                ["ForwardedHeaders:KnownProxies"] = proxies,
                ["ForwardedHeaders:KnownNetworks"] = networks,
            });

    private static async Task<(string? Ip, string? Scheme)> ConnectionAsync(HttpClient client, string peer, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Connection, UriKind.Relative));
        request.Headers.Add(TestClientIp.Header, peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return (body["ip"]?.GetValue<string>(), body["scheme"]?.GetValue<string>());
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : null;
}
