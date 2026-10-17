using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Storage;
using Testcontainers.Minio;

namespace Stockly.Infrastructure.Tests.Support;

/// <summary>One real MinIO per test collection (the docker-compose image, ADR-0008) with a private test bucket.</summary>
public sealed class MinioFixture : IAsyncLifetime
{
    /// <summary>Must equal the minio image in docker-compose.yml (guarded by <c>GuardTests</c>).</summary>
    public const string MinioImage =
        "pgsty/minio:RELEASE.2026-08-04T00-00-00Z@sha256:b6bfe7239bfc83fb90d31612d9704d86039dd714f7904b3f1ad68f211e602372";

    public const string Bucket = "stockly-invoices-test";

    private readonly MinioContainer _container = new MinioBuilder(MinioImage).Build();
    private readonly List<ServiceProvider> _providers = [];

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(_container.GetAccessKey(), _container.GetSecretKey()),
            new AmazonS3Config { ServiceURL = _container.GetConnectionString(), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await client.PutBucketAsync(Bucket);
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>The production storage wiring (<c>AddStocklyStorage</c>) against this container.</summary>
    public IFileStorage CreateStorage(string bucket = Bucket)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Endpoint"] = _container.GetConnectionString(),
                ["Storage:AccessKey"] = _container.GetAccessKey(),
                ["Storage:SecretKey"] = _container.GetSecretKey(),
                ["Storage:Bucket"] = bucket,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStocklyStorage(configuration);

        var provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        return provider.GetRequiredService<IFileStorage>();
    }
}

[CollectionDefinition(Name)]
public sealed class MinioCollectionDefinition : ICollectionFixture<MinioFixture>
{
    public const string Name = "minio";
}
