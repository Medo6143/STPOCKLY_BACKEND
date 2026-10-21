using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using Stockly.Application.Abstractions.Caching;

namespace Stockly.Infrastructure.Caching;

/// <summary>
/// <see cref="IInvoiceStatusCache"/>: <c>stockly:{shopId}:invoice:{id}:status</c>, 10 minutes, overwritten on every
/// change. The key holds the tenant from the caller, so a hit is always the caller's own invoice (rule 10). Redis
/// failures are misses and skipped writes (rule 25).
/// </summary>
internal sealed class RedisInvoiceStatusCache(RedisConnection redis, RedisDegradationLog degradation) : IInvoiceStatusCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public async Task<InvoiceStatusSnapshot?> GetAsync(Guid shopId, Guid invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            var value = await database.StringGetAsync(CacheKeys.InvoiceStatus(shopId, invoiceId)).WaitAsync(cancellationToken);
            return value.IsNull ? null : JsonSerializer.Deserialize<InvoiceStatusSnapshot>(value.ToString(), Json);
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception) || exception is JsonException)
        {
            degradation.Warn(nameof(GetAsync), exception);
            return null;
        }
    }

    public Task SetAsync(Guid shopId, InvoiceStatusSnapshot snapshot, CancellationToken cancellationToken) =>
        WriteAsync(shopId, snapshot, When.Always, nameof(SetAsync), cancellationToken);

    public Task SetIfAbsentAsync(Guid shopId, InvoiceStatusSnapshot snapshot, CancellationToken cancellationToken) =>
        WriteAsync(shopId, snapshot, When.NotExists, nameof(SetIfAbsentAsync), cancellationToken);

    private async Task WriteAsync(
        Guid shopId, InvoiceStatusSnapshot snapshot, When when, string operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var value = JsonSerializer.Serialize(snapshot, Json);
            var database = await redis.GetDatabaseAsync(cancellationToken);
            await database.StringSetAsync(
                    CacheKeys.InvoiceStatus(shopId, snapshot.InvoiceId), value, CacheKeys.InvoiceStatusTimeToLive, when)
                .WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception))
        {
            degradation.Warn(operation, exception);
        }
    }
}
