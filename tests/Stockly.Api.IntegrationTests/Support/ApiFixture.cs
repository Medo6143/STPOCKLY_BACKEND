using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Real PostgreSQL and Redis (the docker-compose images) shared by the Api tests.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    /// <summary>Nothing listens on port 1, so connections fail fast.</summary>
    public const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=stockly;Username=x;Password=x;Timeout=2";

    public const string UnreachableRedis = "127.0.0.1:1,abortConnect=false,connectTimeout=500";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(ComposeImages.Postgres).Build();
    private readonly RedisContainer _redis = new RedisBuilder(ComposeImages.Redis).Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    public StocklyApiFactory CreateFactory(
        string? postgres = null, string? redis = null, bool includeTestEndpoints = false) =>
        new(postgres ?? PostgresConnectionString, redis ?? RedisConnectionString, includeTestEndpoints);
}

[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
