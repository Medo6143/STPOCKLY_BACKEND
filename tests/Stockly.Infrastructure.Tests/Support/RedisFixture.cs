using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Time;
using Testcontainers.Redis;

namespace Stockly.Infrastructure.Tests.Support;

/// <summary>One real Redis per test collection, the docker-compose image.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    /// <summary>Must equal the redis image in docker-compose.yml (guarded by <c>GuardTests</c>).</summary>
    public const string RedisImage =
        "redis:7.4.11-alpine@sha256:858f009f9709ce576febc734aa78b8f6d624b82571f9ddb6bda4377c833b3499";

    /// <summary>Nothing listens on port 1, so connections are refused at once.</summary>
    public const string UnreachableRedis = "127.0.0.1:1,abortConnect=false";

    private readonly RedisContainer _container = new RedisBuilder(RedisImage).Build();
    private readonly List<ServiceProvider> _providers = [];

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>The production cache wiring (<c>AddStocklyCaching</c>) against <paramref name="connectionString"/>.</summary>
    public ICacheService CreateCache(string connectionString, IClock clock, ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddSingleton(clock);
        services.AddStocklyCaching(connectionString);

        var provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        return provider.GetRequiredService<ICacheService>();
    }

    public async Task<TimeSpan?> TimeToLiveAsync(string key)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        return await connection.GetDatabase().KeyTimeToLiveAsync(key);
    }
}

[CollectionDefinition(Name)]
public sealed class RedisCollectionDefinition : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}

/// <summary>Captures log entries so tests can assert on warnings.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new ListLogger(this);

    public void Dispose()
    {
    }

    private void Add(LogLevel level, string message)
    {
        lock (_entries)
        {
            _entries.Add((level, message));
        }
    }

    private sealed class ListLogger(ListLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Add(logLevel, formatter(state, exception));
    }
}
