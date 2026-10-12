using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Stockly.Api.IntegrationTests;

/// <summary>Proves the Api host composes and serves requests in-memory.</summary>
public sealed class ApiHostSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string UnknownRoute = "/this-route-does-not-exist";

    [Fact]
    public async Task Host_UnknownRoute_ReturnsNotFound()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(UnknownRoute, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
