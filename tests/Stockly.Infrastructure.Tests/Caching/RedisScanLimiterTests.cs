using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Features.Invoices;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Caching;

/// <summary>Scan rate limit and daily quota on a real Redis, and with Redis down (docs/design/redis.md §1, §4).</summary>
[Collection(RedisCollectionDefinition.Name)]
public sealed class RedisScanLimiterTests(RedisFixture fixture)
{
    // 2026-10-12 07:00:45 UTC = 10:00:45 in Cairo.
    private readonly FakeClock _clock = new(TestTime.T0.AddSeconds(45));

    [Fact]
    public async Task TryAcquire_UpToTheLimit_AllowedThenDeniedUntilTheNextMinute()
    {
        var limiter = Services(new ScanOptions { RateLimitPerMinute = 3 }).GetRequiredService<IScanRateLimiter>();
        var shopId = Guid.CreateVersion7();

        var decisions = new List<RateLimitDecision>();
        for (var i = 0; i < 4; i++)
        {
            decisions.Add(await limiter.TryAcquireAsync(shopId, CancellationToken.None));
        }

        decisions.Take(3).Should().OnlyContain(decision => decision.Allowed);
        decisions[3].Should().Be(new RateLimitDecision(false, 15));
        var ttl = await fixture.TimeToLiveAsync(CacheKeys.ScanRate(shopId, _clock.UtcNow));
        ttl!.Value.Should().BeCloseTo(CacheKeys.ScanRateTimeToLive, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryAcquire_NextMinute_StartsAFreshCounter()
    {
        var limiter = Services(new ScanOptions { RateLimitPerMinute = 1 }).GetRequiredService<IScanRateLimiter>();
        var shopId = Guid.CreateVersion7();
        await limiter.TryAcquireAsync(shopId, CancellationToken.None);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);

        (await limiter.TryAcquireAsync(shopId, CancellationToken.None)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquire_OtherShop_HasItsOwnCounter()
    {
        var limiter = Services(new ScanOptions { RateLimitPerMinute = 1 }).GetRequiredService<IScanRateLimiter>();
        await limiter.TryAcquireAsync(Guid.CreateVersion7(), CancellationToken.None);

        (await limiter.TryAcquireAsync(Guid.CreateVersion7(), CancellationToken.None)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task TryConsume_UntilTheQuota_ThenExceededWithoutCountingFurther()
    {
        var quota = Services().GetRequiredService<IScanQuotaCounter>();
        var shopId = Guid.CreateVersion7();

        var results = new List<ScanQuotaResult>();
        for (var i = 0; i < 4; i++)
        {
            results.Add(await quota.TryConsumeAsync(shopId, 2, CancellationToken.None));
        }

        results.Should().Equal(ScanQuotaResult.Allowed, ScanQuotaResult.Allowed, ScanQuotaResult.Exceeded, ScanQuotaResult.Exceeded);
        var key = CacheKeys.ScanQuota(shopId, new DateOnly(2026, 10, 12));
        (await fixture.GetAsync(key)).Should().Be("2", "a refused scan is not counted");
        (await fixture.TimeToLiveAsync(key))!.Value.Should().BeCloseTo(CacheKeys.ScanQuotaTimeToLive, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryConsume_CairoDateKey_ChangesAtCairoMidnightNotUtc()
    {
        var quota = Services().GetRequiredService<IScanQuotaCounter>();
        var shopId = Guid.CreateVersion7();
        _clock.UtcNow = new DateTimeOffset(2026, 10, 12, 21, 30, 0, TimeSpan.Zero);

        await quota.TryConsumeAsync(shopId, 5, CancellationToken.None);

        (await fixture.GetAsync(CacheKeys.ScanQuota(shopId, new DateOnly(2026, 10, 13)))).Should().Be("1");
    }

    [Fact]
    public async Task Release_GivesBackOneScanAndNeverGoesBelowZero()
    {
        var quota = Services().GetRequiredService<IScanQuotaCounter>();
        var shopId = Guid.CreateVersion7();
        await quota.TryConsumeAsync(shopId, 1, CancellationToken.None);

        await quota.ReleaseAsync(shopId, CancellationToken.None);
        await quota.ReleaseAsync(shopId, CancellationToken.None);

        (await fixture.GetAsync(CacheKeys.ScanQuota(shopId, new DateOnly(2026, 10, 12)))).Should().Be("0");
        (await quota.TryConsumeAsync(shopId, 1, CancellationToken.None)).Should().Be(ScanQuotaResult.Allowed);
    }

    [Fact]
    public async Task Release_NoCounter_CreatesNoKey()
    {
        var quota = Services().GetRequiredService<IScanQuotaCounter>();
        var shopId = Guid.CreateVersion7();

        await quota.ReleaseAsync(shopId, CancellationToken.None);

        (await fixture.GetAsync(CacheKeys.ScanQuota(shopId, new DateOnly(2026, 10, 12)))).Should().BeNull();
    }

    [Fact]
    public async Task RedisDown_RateFailsOpenQuotaUnavailableWithOneThrottledWarning()
    {
        var logs = new ListLoggerProvider();
        var services = fixture.CreateServices(RedisFixture.UnreachableRedis, _clock, new ScanOptions(), logs);
        var shopId = Guid.CreateVersion7();

        var rate = await services.GetRequiredService<IScanRateLimiter>().TryAcquireAsync(shopId, CancellationToken.None);
        var quota = await services.GetRequiredService<IScanQuotaCounter>().TryConsumeAsync(shopId, 5, CancellationToken.None);
        var release = () => services.GetRequiredService<IScanQuotaCounter>().ReleaseAsync(shopId, CancellationToken.None);

        rate.Allowed.Should().BeTrue("the rate limit fails open (docs/design/redis.md §4)");
        quota.Should().Be(ScanQuotaResult.Unavailable);
        await release.Should().NotThrowAsync();
        logs.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().ContainSingle("the warning is throttled to once a minute");
    }

    [Fact]
    public async Task Reset_UsedUpQuota_DeletesTodaysCounterSoTheFullQuotaIsBack()
    {
        var quota = Services().GetRequiredService<IScanQuotaCounter>();
        var shopId = Guid.CreateVersion7();
        await quota.TryConsumeAsync(shopId, 1, CancellationToken.None);
        (await quota.TryConsumeAsync(shopId, 1, CancellationToken.None)).Should().Be(ScanQuotaResult.Exceeded);

        var reset = await quota.ResetAsync(shopId, CancellationToken.None);

        reset.Should().BeTrue();
        (await fixture.GetAsync(CacheKeys.ScanQuota(shopId, new DateOnly(2026, 10, 12)))).Should().BeNull();
        (await quota.TryConsumeAsync(shopId, 1, CancellationToken.None)).Should().Be(ScanQuotaResult.Allowed);
    }

    [Fact]
    public async Task Reset_OtherShop_KeepsItsCounter()
    {
        var quota = Services().GetRequiredService<IScanQuotaCounter>();
        var other = Guid.CreateVersion7();
        await quota.TryConsumeAsync(other, 1, CancellationToken.None);

        await quota.ResetAsync(Guid.CreateVersion7(), CancellationToken.None);

        (await quota.TryConsumeAsync(other, 1, CancellationToken.None)).Should().Be(ScanQuotaResult.Exceeded);
    }

    [Fact]
    public async Task Reset_RedisDown_ReturnsFalseWithoutThrowing()
    {
        var quota = fixture.CreateServices(RedisFixture.UnreachableRedis, _clock).GetRequiredService<IScanQuotaCounter>();

        (await quota.ResetAsync(Guid.CreateVersion7(), CancellationToken.None)).Should().BeFalse();
    }

    private IServiceProvider Services(ScanOptions? options = null) => fixture.CreateServices(fixture.ConnectionString, _clock, options);
}
