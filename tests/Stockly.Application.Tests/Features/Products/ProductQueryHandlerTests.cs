using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Products;
using Stockly.Application.Features.Products.GetLowStock;
using Stockly.Application.Features.Products.GetProductById;
using Stockly.Application.Features.Products.GetProductMovements;
using Stockly.Application.Features.Products.GetProducts;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;
using Stockly.Domain.Products;
using static Stockly.Application.Tests.Support.ProductTestContext;

namespace Stockly.Application.Tests.Features.Products;

/// <summary>P1, P2 and P11 handlers, including the versioned list cache.</summary>
public sealed class ProductQueryHandlerTests
{
    private static readonly PagedResponse<ProductListItem> Page = new([], 1, 20, 0);

    private readonly ProductTestContext _context = new();

    public ProductQueryHandlerTests()
    {
        _context.Queries.ListAsync(Arg.Any<ProductListFilter>(), Arg.Any<CancellationToken>()).Returns(Page);
        _context.Cache.GetVersionAsync(CacheKeys.CatalogVersion(ShopId), Arg.Any<CancellationToken>()).Returns(7L);
    }

    [Fact]
    public async Task GetProducts_CacheMiss_LoadsFromPostgresAndCachesTenMinutesUnderTheVersionedKey()
    {
        var query = new GetProductsQuery(" بَنادول ", IncludeArchived: false, Page: 2, PageSize: 10);
        var filter = new ProductListFilter("بنادول", LowStockOnly: false, IncludeArchived: false, Page: 2, PageSize: 10);
        var key = $"stockly:{ShopId}:products:list:v7:{CatalogListCache.QueryHash(filter)}";

        var result = await ProductsHandler().Handle(query, CancellationToken.None);

        result.Value.Should().BeSameAs(Page);
        await _context.Queries.Received(1).ListAsync(filter, Arg.Any<CancellationToken>());
        await _context.Cache.Received(1).SetAsync(key, Page, TimeSpan.FromMinutes(10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProducts_CacheHit_DoesNotQueryPostgres()
    {
        var cached = new PagedResponse<ProductListItem>([], 1, 20, 3);
        _context.Cache.GetAsync<PagedResponse<ProductListItem>>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(cached);

        var result = await ProductsHandler().Handle(new GetProductsQuery(null, false, 1, 20), CancellationToken.None);

        result.Value.Should().BeSameAs(cached);
        await _context.Queries.DidNotReceive().ListAsync(Arg.Any<ProductListFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProducts_VersionUnavailable_SkipsTheCacheEntirely()
    {
        _context.Cache.GetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);

        var result = await ProductsHandler().Handle(new GetProductsQuery(null, false, 1, 20), CancellationToken.None);

        result.Value.Should().BeSameAs(Page);
        await _context.Cache.DidNotReceive().GetAsync<PagedResponse<ProductListItem>>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _context.Cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<PagedResponse<ProductListItem>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("ـ")]
    public async Task GetProducts_BlankSearch_IsNoSearch(string? search)
    {
        await ProductsHandler().Handle(new GetProductsQuery(search, true, 1, 20), CancellationToken.None);

        await _context.Queries.Received(1).ListAsync(
            new ProductListFilter(null, false, true, 1, 20), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLowStock_UsesLowStockFilterKeyAndFiveMinutes()
    {
        var filter = new ProductListFilter(null, LowStockOnly: true, IncludeArchived: false, Page: 1, PageSize: 20);
        var key = $"stockly:{ShopId}:products:lowstock:v7:{CatalogListCache.QueryHash(filter)}";

        await LowStockHandler().Handle(new GetLowStockQuery(null, false, 1, 20), CancellationToken.None);

        await _context.Queries.Received(1).ListAsync(filter, Arg.Any<CancellationToken>());
        await _context.Cache.Received(1).SetAsync(key, Page, TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void QueryHash_DiffersForEveryParameter()
    {
        var baseline = new ProductListFilter("x", false, false, 1, 20);
        ProductListFilter[] variants =
        [
            baseline with { NormalizedSearch = "y" },
            baseline with { NormalizedSearch = null },
            baseline with { LowStockOnly = true },
            baseline with { IncludeArchived = true },
            baseline with { Page = 2 },
            baseline with { PageSize = 21 },
        ];

        variants.Select(CatalogListCache.QueryHash).Append(CatalogListCache.QueryHash(baseline))
            .Should().OnlyHaveUniqueItems().And.AllSatisfy(hash => hash.Should().MatchRegex("^[0-9a-f]{32}$"));
        CatalogListCache.QueryHash(baseline).Should().Be(CatalogListCache.QueryHash(baseline with { }));
    }

    [Fact]
    public async Task GetProducts_NoTenant_SkipsTheCache()
    {
        _context.CurrentTenant.ShopId.Returns((Guid?)null);

        await ProductsHandler().Handle(new GetProductsQuery(null, false, 1, 20), CancellationToken.None);

        await _context.Cache.DidNotReceive().GetVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_Existing_ReturnsDetail()
    {
        var id = Guid.CreateVersion7();

        var result = await new GetProductByIdQueryHandler(_context.Queries).Handle(new GetProductByIdQuery(id), CancellationToken.None);

        result.Value.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetById_OtherShopsOrUnknownProduct_ReturnsNotFound()
    {
        _context.Queries.FindDetailAsync(OtherShopsProductId, Arg.Any<CancellationToken>()).Returns((ProductDetail?)null);

        var result = await new GetProductByIdQueryHandler(_context.Queries)
            .Handle(new GetProductByIdQuery(OtherShopsProductId), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
    }

    [Fact]
    public async Task GetMovements_ExistingProduct_ReturnsThePage()
    {
        var id = Guid.CreateVersion7();
        var page = new PagedResponse<StockMovementDto>([], 3, 5, 11);
        _context.Queries.ExistsAsync(id, Arg.Any<CancellationToken>()).Returns(true);
        _context.Queries.ListMovementsAsync(id, 3, 5, Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetProductMovementsQueryHandler(_context.Queries)
            .Handle(new GetProductMovementsQuery(id, 3, 5), CancellationToken.None);

        result.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task GetMovements_OtherShopsProduct_ReturnsNotFound()
    {
        var result = await new GetProductMovementsQueryHandler(_context.Queries)
            .Handle(new GetProductMovementsQuery(OtherShopsProductId, 1, 20), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
        await _context.Queries.DidNotReceive().ListMovementsAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private CatalogListCache ListCache() => new(_context.Cache, _context.CurrentTenant, _context.Queries);

    private GetProductsQueryHandler ProductsHandler() => new(ListCache());

    private GetLowStockQueryHandler LowStockHandler() => new(ListCache());
}
