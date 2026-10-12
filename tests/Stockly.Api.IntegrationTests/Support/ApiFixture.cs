using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Infrastructure;
using Stockly.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Real PostgreSQL (migrated with the production migrations) and Redis (the docker-compose images) shared by the Api tests.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    /// <summary>Nothing listens on port 1, so connections fail fast.</summary>
    public const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=stockly;Username=x;Password=x;Timeout=2";

    public const string UnreachableRedis = "127.0.0.1:1,abortConnect=false,connectTimeout=500";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(ComposeImages.Postgres).Build();
    private readonly RedisContainer _redis = new RedisBuilder(ComposeImages.Redis).Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        var services = new ServiceCollection();
        services.AddPersistence(PostgresConnectionString);
        services.AddSingleton<ICurrentTenant, NoTenant>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StocklyDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    public StocklyApiFactory CreateFactory(
        string? postgres = null,
        string? redis = null,
        bool includeTestEndpoints = false,
        string jwtSigningKey = TestJwt.SigningKey,
        string? authPermitLimit = StocklyApiFactory.RaisedAuthPermitLimit,
        IClock? clock = null) =>
        new(postgres ?? PostgresConnectionString, redis ?? RedisConnectionString, includeTestEndpoints, jwtSigningKey, authPermitLimit, clock);

    /// <summary>Raw SQL for arranging states the API cannot create yet (suspended shop, staff membership...).</summary>
    public async Task<int> ExecuteAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (value, index) in parameters.Select((value, index) => (value, index)))
        {
            command.Parameters.AddWithValue($"p{index}", value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (value, index) in parameters.Select((value, index) => (value, index)))
        {
            command.Parameters.AddWithValue($"p{index}", value);
        }

        return (T?)await command.ExecuteScalarAsync();
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? ShopId => null;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
