using System.Net;
using System.Text.Json.Nodes;
using Stockly.Api.Health;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class HealthCheckTests(ApiFixture fixture)
{
    [Fact]
    public async Task Live_DependenciesDown_StillHealthy()
    {
        await using var factory = fixture.CreateFactory(ApiFixture.UnreachablePostgres, ApiFixture.UnreachableRedis);

        var (status, body) = await GetAsync(factory, HealthEndpoints.Live);

        status.Should().Be(HttpStatusCode.OK);
        body["status"]!.GetValue<string>().Should().Be("Healthy");
        body["checks"]!.AsArray().Should().BeEmpty("liveness never checks dependencies");
    }

    [Fact]
    public async Task Ready_PostgresAndRedisUp_Healthy()
    {
        await using var factory = fixture.CreateFactory();

        var (status, body) = await GetAsync(factory, HealthEndpoints.Ready);

        status.Should().Be(HttpStatusCode.OK);
        body["status"]!.GetValue<string>().Should().Be("Healthy");
        CheckStatus(body, "postgres").Should().Be("Healthy");
        CheckStatus(body, "redis").Should().Be("Healthy");
    }

    [Fact]
    public async Task Ready_RedisDown_DegradedWith200()
    {
        await using var factory = fixture.CreateFactory(redis: ApiFixture.UnreachableRedis);

        var (status, body) = await GetAsync(factory, HealthEndpoints.Ready);

        status.Should().Be(HttpStatusCode.OK, "Redis failure degrades, it never makes the API unready (redis.md §4)");
        body["status"]!.GetValue<string>().Should().Be("Degraded");
        CheckStatus(body, "postgres").Should().Be("Healthy");
        CheckStatus(body, "redis").Should().Be("Degraded");
    }

    [Fact]
    public async Task Ready_PostgresDown_UnhealthyWith503()
    {
        await using var factory = fixture.CreateFactory(postgres: ApiFixture.UnreachablePostgres);

        var (status, body) = await GetAsync(factory, HealthEndpoints.Ready);

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        body["status"]!.GetValue<string>().Should().Be("Unhealthy");
        CheckStatus(body, "postgres").Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Ready_Failure_DoesNotExposeExceptionDetails()
    {
        await using var factory = fixture.CreateFactory(postgres: ApiFixture.UnreachablePostgres);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(HealthEndpoints.Ready, UriKind.Relative));
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        raw.Should().NotContainAny("127.0.0.1", "Exception", "Npgsql", "Password");
    }

    private static async Task<(HttpStatusCode Status, JsonNode Body)> GetAsync(StocklyApiFactory factory, string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return (response.StatusCode, body);
    }

    private static string CheckStatus(JsonNode body, string name) =>
        body["checks"]!.AsArray().Single(check => check!["name"]!.GetValue<string>() == name)!["status"]!.GetValue<string>();
}
