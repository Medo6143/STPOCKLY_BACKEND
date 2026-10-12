using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests;

/// <summary>Proves the Api host composes and serves requests in-memory.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ApiHostSmokeTests(ApiFixture fixture)
{
    private const string UnknownRoute = "/this-route-does-not-exist";

    [Fact]
    public async Task Host_UnknownRoute_ReturnsNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(UnknownRoute, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
