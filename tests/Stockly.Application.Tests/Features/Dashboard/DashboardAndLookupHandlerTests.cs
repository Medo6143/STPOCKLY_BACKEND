using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Dashboard.GetDashboardSummary;
using Stockly.Application.Features.Dashboard.GetExpiringSoon;
using Stockly.Application.Features.Products.LookupProduct;
using Stockly.Contracts.Common;
using Stockly.Contracts.Dashboard;
using Stockly.Contracts.Products;
using Stockly.Domain.Products;

namespace Stockly.Application.Tests.Features.Dashboard;

/// <summary>D1, D2 and P12 handlers: the Cairo day, the versioned cache and Redis down, the expiry window, the lookup.</summary>
public sealed class DashboardAndLookupHandlerTests
{
    private static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");

    // 21:30 UTC on 17 October is 00:30 on 18 October in Cairo (UTC+3 until the end of October 2026).
    private static readonly DateTimeOffset AfterCairoMidnight = new(2026, 10, 17, 21, 30, 0, TimeSpan.Zero);

    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IDashboardQueries _queries = Substitute.For<IDashboardQueries>();

    public DashboardAndLookupHandlerTests()
    {
        _clock.UtcNow.Returns(AfterCairoMidnight);
        _tenant.ShopId.Returns(ShopId);
        _cache.GetVersionAsync(CacheKeys.CatalogVersion(ShopId), Arg.Any<CancellationToken>()).Returns(7L);
        _queries.GetCountsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new DashboardCounts(10, 3, 1, 2, 4, 120.5m, 1, 5));
    }

    [Fact]
    public async Task Summary_Miss_CountsTheCairoDayAndCachesTwoMinutesUnderVersionAndDate()
    {
        var result = await Summary();

        result.Should().Be(new DashboardSummary(10, 3, 1, 2, new TodaySales(4, "120.50", 1), 5, AfterCairoMidnight.UtcDateTime));
        await _queries.Received(1).GetCountsAsync(
            new DateTimeOffset(2026, 10, 17, 21, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 18, 21, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 12, 17),
            Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(
            $"stockly:{ShopId:D}:dashboard:summary:v7:20261018", result, TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Summary_Hit_ServedFromCacheWithoutPostgres()
    {
        var cached = new DashboardSummary(1, 0, 0, 0, new TodaySales(0, "0.00", 0), 0, AfterCairoMidnight.UtcDateTime);
        _cache.GetAsync<DashboardSummary>(CacheKeys.DashboardSummary(ShopId, 7, new DateOnly(2026, 10, 18)), Arg.Any<CancellationToken>())
            .Returns(cached);

        (await Summary()).Should().BeSameAs(cached);
        await _queries.DidNotReceiveWithAnyArgs().GetCountsAsync(default, default, default, default);
    }

    [Fact]
    public async Task Summary_VersionBumped_NewKeyMisses()
    {
        _cache.GetAsync<DashboardSummary>(CacheKeys.DashboardSummary(ShopId, 7, new DateOnly(2026, 10, 18)), Arg.Any<CancellationToken>())
            .Returns(new DashboardSummary(1, 0, 0, 0, new TodaySales(0, "0.00", 0), 0, AfterCairoMidnight.UtcDateTime));
        _cache.GetVersionAsync(CacheKeys.CatalogVersion(ShopId), Arg.Any<CancellationToken>()).Returns(8L);

        (await Summary()).ProductCount.Should().Be(10);
    }

    [Fact]
    public async Task Summary_RedisDown_SkipsTheCacheAndReadsPostgres()
    {
        _cache.GetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);

        (await Summary()).ProductCount.Should().Be(10);
        await _cache.DidNotReceiveWithAnyArgs().SetAsync<DashboardSummary>(default!, default!, default, default);
    }

    [Fact]
    public void CairoDayRange_BeforeAndAfterCairoMidnight()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 10, 17, 20, 59, 59, TimeSpan.Zero));
        _clock.CairoDayRangeUtc().Should().Be((
            new DateTimeOffset(2026, 10, 16, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 17, 21, 0, 0, TimeSpan.Zero)));

        _clock.UtcNow.Returns(AfterCairoMidnight);
        _clock.CairoDayRangeUtc().Start.Should().Be(new DateTimeOffset(2026, 10, 17, 21, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ExpiringSoon_WindowEndsDaysAfterTheCairoDate()
    {
        var page = new PagedResponse<ExpiringItem>([], 2, 10, 0);
        _queries.ListExpiringAsync(new DateOnly(2026, 10, 25), 2, 10, Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetExpiringSoonQueryHandler(_queries, _clock).Handle(new GetExpiringSoonQuery(7, 2, 10), CancellationToken.None);

        result.Value.Should().BeSameAs(page);
    }

    [Theory]
    [InlineData(0, 1, 20, false)]
    [InlineData(366, 1, 20, false)]
    [InlineData(365, 1, 100, true)]
    [InlineData(60, 1, 101, false)]
    [InlineData(60, 0, 20, false)]
    public async Task ExpiringSoon_Validator(int days, int page, int pageSize, bool valid)
    {
        (await new GetExpiringSoonQueryValidator().ValidateAsync(new GetExpiringSoonQuery(days, page, pageSize))).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Lookup_TrimsTheBarcode_NotFoundIsProductNotFound()
    {
        var queries = Substitute.For<IProductQueries>();
        var found = new ProductLookupResponse(
            new ProductListItem(Guid.CreateVersion7(), "x", "1.000", new BaseUnitSummary(Guid.CreateVersion7(), "u", "1.00"), "0.000", false, false),
            new UnitDto(Guid.CreateVersion7(), "u", "1.000000", "1.00", "622", true));
        queries.FindByBarcodeAsync("622", Arg.Any<CancellationToken>()).Returns(found);
        var handler = new LookupProductQueryHandler(queries);

        (await handler.Handle(new LookupProductQuery(" 622 "), CancellationToken.None)).Value.Should().BeSameAs(found);
        (await handler.Handle(new LookupProductQuery("999"), CancellationToken.None)).Error.Should().Be(ProductErrors.NotFound);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("6221234567890", true)]
    public async Task Lookup_Validator(string? barcode, bool valid)
    {
        (await new LookupProductQueryValidator().ValidateAsync(new LookupProductQuery(barcode))).IsValid.Should().Be(valid);
        (await new LookupProductQueryValidator().ValidateAsync(new LookupProductQuery(new string('1', Product.BarcodeMaxLength + 1))))
            .IsValid.Should().BeFalse();
    }

    private async Task<DashboardSummary> Summary() =>
        (await new GetDashboardSummaryQueryHandler(_tenant, _cache, _queries, _clock).Handle(new GetDashboardSummaryQuery(), CancellationToken.None)).Value;
}
