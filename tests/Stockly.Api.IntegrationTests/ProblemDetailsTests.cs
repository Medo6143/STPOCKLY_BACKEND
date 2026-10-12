using System.Net;
using System.Text.Json.Nodes;
using Stockly.Api.IntegrationTests.Support;
using Stockly.Api.Middleware;

namespace Stockly.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class ProblemDetailsTests(ApiFixture fixture)
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/nope", UriKind.Relative));
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be(ProblemJson);
        problem["status"]!.GetValue<int>().Should().Be(404);
        problem["traceId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        problem["correlationId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        problem["instance"]!.GetValue<string>().Should().Be("GET /nope");
    }

    [Fact]
    public async Task UnhandledException_Returns500ProblemWithoutDetails()
    {
        await using var factory = fixture.CreateFactory(includeTestEndpoints: true);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/__tests/throw", UriKind.Relative));
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        var problem = JsonNode.Parse(raw)!;

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be(ProblemJson);
        problem["status"]!.GetValue<int>().Should().Be(500);
        problem["title"]!.GetValue<string>().Should().Be("An unexpected error occurred.");
        problem["traceId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        problem["correlationId"]!.GetValue<string>().Should().Be("abc-123");
        raw.Should().NotContain("hunter2").And.NotContain("InvalidOperationException").And.NotContain("at Stockly");
        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().Equal("abc-123");
    }
}
