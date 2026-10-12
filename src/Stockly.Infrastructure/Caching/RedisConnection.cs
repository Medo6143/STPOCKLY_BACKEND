using StackExchange.Redis;

namespace Stockly.Infrastructure.Caching;

/// <summary>
/// The one Redis connection of the process, opened on first use so that startup never waits for (or fails on) Redis.
/// <c>AbortOnConnectFail = false</c>: an unreachable Redis does not throw; the multiplexer keeps reconnecting in the
/// background and, while disconnected, each operation fails at once (<see cref="BacklogPolicy.FailFast"/>), so the
/// caller falls back to PostgreSQL (docs/design/redis.md §4).
/// </summary>
internal sealed class RedisConnection : IAsyncDisposable
{
    /// <summary>Operation timeout: a dead Redis costs a request at most this long (docs/design/redis.md §4).</summary>
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>The first connect may take longer than an operation (TLS, AUTH); it happens once per process.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1);

    private readonly Lazy<Task<IConnectionMultiplexer>> _multiplexer;

    public RedisConnection(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _multiplexer = new Lazy<Task<IConnectionMultiplexer>>(() => ConnectAsync(connectionString));
    }

    public Task<IConnectionMultiplexer> GetMultiplexerAsync() => _multiplexer.Value;

    public async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken) =>
        (await _multiplexer.Value.WaitAsync(cancellationToken)).GetDatabase();

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer.IsValueCreated && _multiplexer.Value.IsCompletedSuccessfully)
        {
            await (await _multiplexer.Value).DisposeAsync();
        }
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
        options.SyncTimeout = (int)OperationTimeout.TotalMilliseconds;
        options.AsyncTimeout = (int)OperationTimeout.TotalMilliseconds;

        // While disconnected, fail each command at once instead of queueing it until the connection returns.
        options.BacklogPolicy = BacklogPolicy.FailFast;
        return await ConnectionMultiplexer.ConnectAsync(options);
    }
}
