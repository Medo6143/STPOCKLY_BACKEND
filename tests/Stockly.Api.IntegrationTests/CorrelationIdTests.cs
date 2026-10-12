using Stockly.Api.Health;
using Stockly.Api.IntegrationTests.Support;
using Stockly.Api.Middleware;

namespace Stockly.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class CorrelationIdTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("abc-123")]
    [InlineData("0192f0a0.0000_7000-8000")]
    [InlineData("a234567890123456789012345678901234567890123456789012345678901234")] // 64 chars
    public async Task Request_ValidCorrelationId_IsEchoed(string correlationId)
    {
        var echoed = await SendAsync(correlationId);

        echoed.Should().Be(correlationId);
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("new\nline")]
    [InlineData("<script>")]
    [InlineData("a2345678901234567890123456789012345678901234567890123456789012345")] // 65 chars
    public async Task Request_InvalidCorrelationId_IsReplaced(string correlationId)
    {
        var echoed = await SendAsync(correlationId);

        echoed.Should().NotBe(correlationId).And.MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Request_WithoutCorrelationId_GetsNewOne()
    {
        var first = await SendAsync(correlationId: null);
        var second = await SendAsync(correlationId: null);

        first.Should().MatchRegex("^[0-9a-f]{32}$");
        second.Should().NotBe(first);
    }

    private async Task<string> SendAsync(string? correlationId)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(HealthEndpoints.Live, UriKind.Relative));
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        using var response = await client.SendAsync(request);
        return response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
    }
}
