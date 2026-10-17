using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Caching;
using Stockly.Domain.Invoices;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Caching;

/// <summary>The AI result cache and the invoice status cache on a real Redis, and with Redis down (rule 25).</summary>
[Collection(RedisCollectionDefinition.Name)]
public sealed class RedisInvoiceCachesTests(RedisFixture fixture)
{
    private const string Model = "google/gemini-2.5-flash";
    private const string CurrentPromptVersion = "invoice-extraction-v1";

    private static readonly ExtractedInvoice Extraction = new(
        "شركة الأدوية", "INV-1", "2026-10-10", "125.00", false, [new ExtractedInvoiceLine("بنادول", "علبة", "10", "12.50", "125.00", null, null)]);

    private readonly FakeClock _clock = new(TestTime.T0);

    [Fact]
    public async Task AiCache_SetThenGet_RoundTripsWithModelPromptVersionAndSevenDayTtl()
    {
        var cache = Services().GetRequiredService<IAiResultCache>();
        var shopId = Guid.CreateVersion7();
        var sha = Sha();

        await cache.SetAsync(shopId, sha, Model, Extraction, CancellationToken.None);
        var cached = await cache.GetAsync(shopId, sha, CancellationToken.None);

        cached.Should().NotBeNull();
        cached!.Model.Should().Be(Model);
        cached.PromptVersion.Should().Be(CurrentPromptVersion);
        cached.Extraction.Should().BeEquivalentTo(Extraction);
        (await fixture.TimeToLiveAsync(CacheKeys.AiInvoice(shopId, sha)))!.Value
            .Should().BeCloseTo(CacheKeys.AiResultTimeToLive, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AiCache_OtherShopSameImage_Miss()
    {
        var cache = Services().GetRequiredService<IAiResultCache>();
        var sha = Sha();
        await cache.SetAsync(Guid.CreateVersion7(), sha, Model, Extraction, CancellationToken.None);

        (await cache.GetAsync(Guid.CreateVersion7(), sha, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task AiCache_ValueFromAnotherPromptVersion_Miss()
    {
        var cache = Services().GetRequiredService<IAiResultCache>();
        var shopId = Guid.CreateVersion7();
        var sha = Sha();
        await fixture.SetAsync(
            CacheKeys.AiInvoice(shopId, sha),
            """{"promptVersion":"invoice-extraction-v0","model":"m","extraction":{"supplierName":null,"invoiceNumber":null,"invoiceDate":null,"totalAmount":null,"truncated":false,"items":[]}}""");

        (await cache.GetAsync(shopId, sha, CancellationToken.None)).Should().BeNull("a prompt change invalidates old extractions");
    }

    [Fact]
    public async Task AiCache_UnreadableValue_Miss()
    {
        var cache = Services().GetRequiredService<IAiResultCache>();
        var shopId = Guid.CreateVersion7();
        var sha = Sha();
        await fixture.SetAsync(CacheKeys.AiInvoice(shopId, sha), "not json");

        (await cache.GetAsync(shopId, sha, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task StatusCache_SetThenGet_RoundTripsWithTenMinuteTtl()
    {
        var cache = Services().GetRequiredService<IInvoiceStatusCache>();
        var shopId = Guid.CreateVersion7();
        var snapshot = new InvoiceStatusSnapshot(Guid.CreateVersion7(), InvoiceStatus.NeedsReview, TestTime.T1, Guid.CreateVersion7(), TestTime.T0);

        await cache.SetAsync(shopId, snapshot, CancellationToken.None);

        (await cache.GetAsync(shopId, snapshot.InvoiceId, CancellationToken.None)).Should().Be(snapshot);
        (await fixture.GetAsync(CacheKeys.InvoiceStatus(shopId, snapshot.InvoiceId))).Should().Contain("\"NeedsReview\"");
        (await fixture.TimeToLiveAsync(CacheKeys.InvoiceStatus(shopId, snapshot.InvoiceId)))!.Value
            .Should().BeCloseTo(CacheKeys.InvoiceStatusTimeToLive, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task StatusCache_OtherShopsKey_Miss()
    {
        var cache = Services().GetRequiredService<IInvoiceStatusCache>();
        var snapshot = new InvoiceStatusSnapshot(Guid.CreateVersion7(), InvoiceStatus.Processing, TestTime.T0, Guid.CreateVersion7(), TestTime.T0);
        await cache.SetAsync(Guid.CreateVersion7(), snapshot, CancellationToken.None);

        (await cache.GetAsync(Guid.CreateVersion7(), snapshot.InvoiceId, CancellationToken.None)).Should().BeNull("wrong tenant is a miss (rule 10)");
    }

    [Fact]
    public async Task RedisDown_BothCachesMissAndSkipWritesWithoutThrowing()
    {
        var logs = new ListLoggerProvider();
        var services = fixture.CreateServices(RedisFixture.UnreachableRedis, _clock, logs: logs);
        var aiCache = services.GetRequiredService<IAiResultCache>();
        var statusCache = services.GetRequiredService<IInvoiceStatusCache>();
        var shopId = Guid.CreateVersion7();
        var snapshot = new InvoiceStatusSnapshot(Guid.CreateVersion7(), InvoiceStatus.Processing, TestTime.T0, Guid.CreateVersion7(), TestTime.T0);

        (await aiCache.GetAsync(shopId, Sha(), CancellationToken.None)).Should().BeNull();
        await aiCache.SetAsync(shopId, Sha(), Model, Extraction, CancellationToken.None);
        (await statusCache.GetAsync(shopId, snapshot.InvoiceId, CancellationToken.None)).Should().BeNull();
        await statusCache.SetAsync(shopId, snapshot, CancellationToken.None);

        logs.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().ContainSingle();
    }

    private static string Sha() => Convert.ToHexStringLower(Guid.CreateVersion7().ToByteArray()) + new string('0', 32);

    private IServiceProvider Services() => fixture.CreateServices(fixture.ConnectionString, _clock);
}
