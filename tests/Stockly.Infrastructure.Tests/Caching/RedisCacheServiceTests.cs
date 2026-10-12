using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Caching;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Caching;

/// <summary><c>RedisCacheService</c> on a real Redis, and with Redis down (docs/design/redis.md §4, AGENTS.md rule 25).</summary>
[Collection(RedisCollectionDefinition.Name)]
public sealed class RedisCacheServiceTests(RedisFixture fixture)
{
    private readonly FakeClock _clock = new(TestTime.T0);

    [Fact]
    public async Task SetThenGet_RoundTripsTheDtoWithItsTimeToLive()
    {
        var cache = fixture.CreateCache(fixture.ConnectionString, _clock);
        var key = Key("products:list:v1:abc");
        var value = new SampleDto("بنادول", "12.50", [1, 2, 3]);

        await cache.SetAsync(key, value, TimeSpan.FromMinutes(10), CancellationToken.None);
        var read = await cache.GetAsync<SampleDto>(key, CancellationToken.None);

        read.Should().BeEquivalentTo(value);
        var ttl = await fixture.TimeToLiveAsync(key);
        ttl.Should().NotBeNull("every value key has a TTL (AGENTS.md rule 22)");
        ttl!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsNull()
    {
        var cache = fixture.CreateCache(fixture.ConnectionString, _clock);

        (await cache.GetAsync<SampleDto>(Key("missing"), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Version_NeverBumped_IsZeroAndEachIncrementAddsOne()
    {
        var cache = fixture.CreateCache(fixture.ConnectionString, _clock);
        var key = Key("ver:catalog");

        (await cache.GetVersionAsync(key, CancellationToken.None)).Should().Be(0);
        await cache.IncrementVersionAsync(key, CancellationToken.None);
        await cache.IncrementVersionAsync(key, CancellationToken.None);

        (await cache.GetVersionAsync(key, CancellationToken.None)).Should().Be(2);
        (await fixture.TimeToLiveAsync(key)).Should().BeNull("version counters are tiny and never expire");
    }

    [Fact]
    public async Task RedisDown_EveryOperationDegradesWithoutThrowing()
    {
        var logs = new ListLoggerProvider();
        var cache = fixture.CreateCache(RedisFixture.UnreachableRedis, _clock, logs);
        var key = Key("products:list:v1:abc");

        var read = await cache.GetAsync<SampleDto>(key, CancellationToken.None);
        await cache.SetAsync(key, new SampleDto("x", "1.00", []), TimeSpan.FromMinutes(1), CancellationToken.None);
        var version = await cache.GetVersionAsync(Key("ver:catalog"), CancellationToken.None);
        await cache.IncrementVersionAsync(Key("ver:catalog"), CancellationToken.None);

        read.Should().BeNull();
        version.Should().BeNull("an unknown version must skip the cache, never read v0");
        logs.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().ContainSingle("warnings are throttled to one a minute");
    }

    [Fact]
    public async Task RedisDown_WarnsAgainAfterAMinute_AndNeverLogsTheConnectionString()
    {
        var logs = new ListLoggerProvider();
        var cache = fixture.CreateCache(RedisFixture.UnreachableRedis, _clock, logs);

        await cache.GetVersionAsync(Key("ver:catalog"), CancellationToken.None);
        _clock.UtcNow = TestTime.T0.AddSeconds(59);
        await cache.GetVersionAsync(Key("ver:catalog"), CancellationToken.None);
        _clock.UtcNow = TestTime.T0.AddMinutes(1);
        await cache.GetVersionAsync(Key("ver:catalog"), CancellationToken.None);

        var warnings = logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(2);
        warnings.Should().AllSatisfy(entry => entry.Message.Should().NotContain("127.0.0.1"));
    }

    [Fact]
    public async Task RedisDown_OperationsFailFast()
    {
        var cache = fixture.CreateCache(RedisFixture.UnreachableRedis, _clock);
        await cache.GetVersionAsync(Key("warm-up"), CancellationToken.None);

        var started = DateTime.UtcNow;
        await cache.GetAsync<SampleDto>(Key("any"), CancellationToken.None);
        await cache.GetVersionAsync(Key("ver:catalog"), CancellationToken.None);

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "a dead Redis costs a request at most the 250 ms operation timeout");
    }

    private static string Key(string suffix) => CacheKeys.CatalogVersion(Guid.CreateVersion7()).Replace("ver:catalog", suffix, StringComparison.Ordinal);

    private sealed record SampleDto(string Name, string Price, IReadOnlyList<int> Numbers);
}
