using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Infrastructure;
using Stockly.Infrastructure.Persistence;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>
/// Real PostgreSQL (migrated with the production migrations), Redis and MinIO (the docker-compose images) shared by the
/// Api tests. MinIO has a private bucket for invoice images.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    /// <summary>Nothing listens on port 1, so connections fail fast.</summary>
    public const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=stockly;Username=x;Password=x;Timeout=2";

    public const string UnreachableRedis = "127.0.0.1:1,abortConnect=false,connectTimeout=500";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(ComposeImages.Postgres).Build();
    private readonly RedisContainer _redis = new RedisBuilder(ComposeImages.Redis).Build();
    private readonly MinioContainer _minio = new MinioBuilder(ComposeImages.Minio).Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public StorageSettings Storage => new(_minio.GetConnectionString(), _minio.GetAccessKey(), _minio.GetSecretKey(), StorageSettings.TestBucket);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _minio.StartAsync());
        using (var s3 = new AmazonS3Client(
            new BasicAWSCredentials(Storage.AccessKey, Storage.SecretKey),
            new AmazonS3Config { ServiceURL = Storage.Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" }))
        {
            await s3.PutBucketAsync(StorageSettings.TestBucket);
        }

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
        await _minio.DisposeAsync();
    }

    public StocklyApiFactory CreateFactory(
        string? postgres = null,
        string? redis = null,
        bool includeTestEndpoints = false,
        string jwtSigningKey = TestJwt.SigningKey,
        string? authPermitLimit = StocklyApiFactory.RaisedAuthPermitLimit,
        IClock? clock = null,
        StocklyApiFactory.JobMode jobs = StocklyApiFactory.JobMode.Off) =>
        new(
            postgres ?? PostgresConnectionString,
            redis ?? RedisConnectionString,
            includeTestEndpoints,
            jwtSigningKey,
            authPermitLimit,
            clock,
            Storage,
            jobs,
            seedAdmin: postgres is null);

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
