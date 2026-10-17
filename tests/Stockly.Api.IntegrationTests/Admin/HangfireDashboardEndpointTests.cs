using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Admin;

/// <summary>
/// Contract §11.3: the Hangfire dashboard opens only through a one-time code (60 s, single use) exchanged for an
/// HttpOnly, SameSite=Strict cookie scoped to /hangfire.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class HangfireDashboardEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Session_CodeOpensTheDashboardOnce_CookieIsHttpOnlyStrictAndScoped()
    {
        await using var factory = fixture.CreateFactory();
        using var api = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(api);
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var session = await AdminApi.PostAsync(api, AdminApi.HangfireSession, new { }, admin.AccessToken);
        var url = session.Body!["url"]!.GetValue<string>();
        using var exchange = await browser.GetAsync(new Uri(url, UriKind.Relative));
        using var reuse = await browser.GetAsync(new Uri(url, UriKind.Relative));
        var cookie = exchange.Headers.GetValues("Set-Cookie").Single();
        using var dashboard = await GetWithCookieAsync(browser, "/hangfire/", cookie.Split(';')[0]);
        using var withoutCookie = await browser.GetAsync(new Uri("/hangfire/", UriKind.Relative));

        session.Status.Should().Be(HttpStatusCode.OK, session.Raw);
        url.Should().StartWith("/hangfire?code=");
        exchange.StatusCode.Should().Be(HttpStatusCode.Redirect);
        exchange.Headers.Location!.ToString().Should().Be("/hangfire/");
        cookie.Should().StartWith("stockly_hangfire=").And.Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/hangfire");
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a code is single use");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync()).Should().Contain("Hangfire");
        withoutCookie.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_ForgedCookieOrUnknownCode_401()
    {
        await using var factory = fixture.CreateFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        using var forged = await GetWithCookieAsync(browser, "/hangfire/", "stockly_hangfire=not-a-protected-value");
        using var unknownCode = await browser.GetAsync(new Uri("/hangfire?code=unknown", UriKind.Relative));

        forged.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownCode.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_ExpiredCookie_401()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var factory = fixture.CreateFactory(clock: clock);
        using var api = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(api);
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var url = (await AdminApi.PostAsync(api, AdminApi.HangfireSession, new { }, admin.AccessToken)).Body!["url"]!.GetValue<string>();
        using var exchange = await browser.GetAsync(new Uri(url, UriKind.Relative));
        var cookie = exchange.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        using var expired = await GetWithCookieAsync(browser, "/hangfire/", cookie);

        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the dashboard cookie is valid 15 minutes");
    }

    [Fact]
    public async Task Session_IsNotInTheOpenApiDocument()
    {
        var document = await OpenApiSpec.LoadRuntimeAsync();

        document["paths"]!.AsObject().Select(path => path.Key).Should().NotContain(path => path.Contains("hangfire", StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> GetWithCookieAsync(HttpClient client, string path, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }
}
