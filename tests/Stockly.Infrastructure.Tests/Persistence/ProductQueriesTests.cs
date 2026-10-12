using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Products;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;
using Stockly.Domain.Stock;
using MovementReason = Stockly.Domain.Stock.MovementReason;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>The catalog read projections (contract P1, P2, P11) and the product write repository on a real PostgreSQL.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ProductQueriesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task List_SortsByNormalizedNameAndPages()
    {
        var shop = await fixture.CreateShopAsync();
        await SaveAsync(shop, Catalog.NewProduct(shop.Id, "جـ ثالث"), Catalog.NewProduct(shop.Id, "أ أول"), Catalog.NewProduct(shop.Id, "ب ثاني"));

        var first = await ListAsync(shop, new ProductListFilter(null, false, false, Page: 1, PageSize: 2));
        var second = await ListAsync(shop, new ProductListFilter(null, false, false, Page: 2, PageSize: 2));

        first.Items.Select(item => item.Name).Should().Equal("أ أول", "ب ثاني");
        second.Items.Select(item => item.Name).Should().Equal("جـ ثالث");
        first.TotalCount.Should().Be(3);
        second.TotalCount.Should().Be(3);
        second.Page.Should().Be(2);
        second.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task List_Item_HasFormattedValuesAndTheBaseUnit()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        product.AddUnit("علبة", 3m, 40m, null);
        product.RecordMovement(-1.5m, MovementReason.ManualAdjustment, MovementSource.None, Catalog.UserId, "x", TestTime.T1);
        await SaveAsync(shop, product);

        var item = (await ListAsync(shop, new ProductListFilter(null, false, false, 1, 20))).Items.Single();

        item.Should().BeEquivalentTo(new ProductListItem(
            product.Id, "بنادول اكسترا", "-1.500", new BaseUnitSummary(product.BaseUnit.Id, "شريط", "15.00"), "5.000", true, false));
    }

    [Theory]
    [InlineData("بنادول")]
    [InlineData("بَنادول")]
    [InlineData("اكسترا")]
    [InlineData("إكسترا")]
    [InlineData("EXTRA")]
    public async Task List_Search_MatchesTheArabicNormalizedNameAnywhere(string term)
    {
        var shop = await fixture.CreateShopAsync();
        await SaveAsync(shop, Catalog.NewProduct(shop.Id, "بنادول اكسترا Extra"), Catalog.NewProduct(shop.Id, "كونجستال"));

        var page = await ListAsync(shop, new ProductListFilter(Domain.Text.ArabicTextNormalizer.Normalize(term), false, false, 1, 20));

        page.Items.Select(item => item.Name).Should().Equal("بنادول اكسترا Extra");
        page.TotalCount.Should().Be(1);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task List_SearchWildcards_AreMatchedLiterally(string term)
    {
        var shop = await fixture.CreateShopAsync();
        await SaveAsync(shop, Catalog.NewProduct(shop.Id, "بنادول"), Catalog.NewProduct(shop.Id, $"خصم 50{term} علبة"));

        var page = await ListAsync(shop, new ProductListFilter(term, false, false, 1, 20));

        page.Items.Should().ContainSingle().Which.Name.Should().Contain(term);
    }

    [Fact]
    public async Task List_LowStockAndArchivedFilters()
    {
        var shop = await fixture.CreateShopAsync();
        var low = Catalog.NewProduct(shop.Id, "منخفض");
        var plenty = Catalog.NewProduct(shop.Id, "متوفر");
        plenty.RecordMovement(50m, MovementReason.StockCount, MovementSource.None, Catalog.UserId, null, TestTime.T1);
        var archivedLow = Catalog.NewProduct(shop.Id, "مؤرشف");
        archivedLow.Archive();
        await SaveAsync(shop, low, plenty, archivedLow);

        var lowOnly = await ListAsync(shop, new ProductListFilter(null, LowStockOnly: true, IncludeArchived: false, 1, 20));
        var lowWithArchived = await ListAsync(shop, new ProductListFilter(null, LowStockOnly: true, IncludeArchived: true, 1, 20));
        var all = await ListAsync(shop, new ProductListFilter(null, LowStockOnly: false, IncludeArchived: false, 1, 20));

        lowOnly.Items.Select(item => item.Name).Should().Equal("منخفض");
        lowWithArchived.Items.Select(item => item.Name).Should().BeEquivalentTo("منخفض", "مؤرشف");
        lowWithArchived.Items.Single(item => item.Name == "مؤرشف").IsArchived.Should().BeTrue();
        all.Items.Select(item => item.Name).Should().BeEquivalentTo("منخفض", "متوفر");
    }

    [Fact]
    public async Task List_OnlyTheCurrentShopsProducts()
    {
        var shop = await fixture.CreateShopAsync();
        var otherShop = await fixture.CreateShopAsync("متجر آخر");
        await SaveAsync(shop, Catalog.NewProduct(shop.Id, "لي"));
        await SaveAsync(otherShop, Catalog.NewProduct(otherShop.Id, "ليس لي"));

        var page = await ListAsync(shop, new ProductListFilter(null, false, true, 1, 20));

        page.Items.Select(item => item.Name).Should().Equal("لي");
    }

    [Fact]
    public async Task Detail_HasUnitsBaseFirstThenByFactorAndTimestamps()
    {
        var clock = new FakeClock(TestTime.T2);
        var shop = await fixture.CreateShopAsync();
        var product = Product.Create(shop.Id, "بنادول", "شريط", 12.50m, 15m, 5m, TestTime.T0, " 622 ", " مسكنات ").Value;
        product.AddUnit("كرتونة", 30m, 400m, "C1");
        product.AddUnit("نصف شريط", 0.5m, 8m, null);
        product.AddUnit("علبة", 3m, 40m, null);
        await using (var context = fixture.CreateContext(shop.Id, clock))
        {
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        var detail = await QueriesAsync(shop, queries => queries.FindDetailAsync(product.Id, CancellationToken.None));

        detail!.Units.Select(unit => (unit.Name, unit.Factor)).Should().Equal(
            ("شريط", "1.000000"), ("نصف شريط", "0.500000"), ("علبة", "3.000000"), ("كرتونة", "30.000000"));
        detail.Units[0].IsBase.Should().BeTrue();
        detail.Units[3].Barcode.Should().Be("C1");
        detail.Barcode.Should().Be("622");
        detail.Category.Should().Be("مسكنات");
        detail.CostPrice.Should().Be("12.50");
        detail.Quantity.Should().Be("0.000");
        detail.LowStockThreshold.Should().Be("5.000");
        detail.IsLowStock.Should().BeTrue();
        detail.CreatedAt.Should().Be(TestTime.T0.UtcDateTime);
        detail.UpdatedAt.Should().Be(TestTime.T2.UtcDateTime);
        detail.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task DetailAndExists_OtherShopsProduct_AreNotFound()
    {
        var shop = await fixture.CreateShopAsync();
        var otherShop = await fixture.CreateShopAsync("متجر آخر");
        var product = Catalog.NewProduct(otherShop.Id);
        await SaveAsync(otherShop, product);

        (await QueriesAsync(shop, queries => queries.FindDetailAsync(product.Id, CancellationToken.None))).Should().BeNull();
        (await QueriesAsync(shop, queries => queries.ExistsAsync(product.Id, CancellationToken.None))).Should().BeFalse();
        (await QueriesAsync(otherShop, queries => queries.ExistsAsync(product.Id, CancellationToken.None))).Should().BeTrue();
    }

    [Fact]
    public async Task Movements_NewestFirstAndPaged()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        product.RecordMovement(10m, MovementReason.StockCount, MovementSource.None, Catalog.UserId, null, TestTime.T0);
        var second = product.RecordMovement(-2m, MovementReason.ManualAdjustment, MovementSource.None, Catalog.UserId, "تالف", TestTime.T1).Value;
        var third = product.RecordMovement(5m, MovementReason.ManualAdjustment, MovementSource.None, Catalog.UserId, "وجدت", TestTime.T2).Value;
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Products.Add(product);
            context.StockMovements.AddRange(
                product.RecordMovement(0.5m, MovementReason.ManualAdjustment, MovementSource.None, Catalog.UserId, "dated earlier", TestTime.T0.AddDays(-1)).Value,
                second,
                third);
            await context.SaveChangesAsync();
        }

        var page = await QueriesAsync(shop, queries => queries.ListMovementsAsync(product.Id, 1, 2, CancellationToken.None));

        page.TotalCount.Should().Be(3);
        page.Items.Select(item => item.Id).Should().Equal(third.Id, second.Id);
        page.Items[1].Should().BeEquivalentTo(new StockMovementDto(
            second.Id, product.Id, "-2.000", "8.000", Contracts.Products.MovementReason.ManualAdjustment, null, null, "تالف", Catalog.UserId, TestTime.T1.UtcDateTime));
    }

    [Fact]
    public async Task Repository_BarcodeChecks_IgnoreArchivedProductsTheProductItselfAndOtherShops()
    {
        var shop = await fixture.CreateShopAsync();
        var otherShop = await fixture.CreateShopAsync("متجر آخر");
        var active = Product.Create(shop.Id, "a", "u", 1m, 1m, 0m, TestTime.T0, "111").Value;
        var archived = Product.Create(shop.Id, "b", "u", 1m, 1m, 0m, TestTime.T0, "222").Value;
        archived.Archive();
        active.AddUnit("علبة", 3m, 40m, "U1");
        await SaveAsync(shop, active, archived);
        await SaveAsync(otherShop, Product.Create(otherShop.Id, "c", "u", 1m, 1m, 0m, TestTime.T0, "333").Value);

        await using var scope = fixture.CreateScope(shop.Id);
        var products = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitId = active.Units.Single(unit => !unit.IsBase).Id;

        (await products.IsBarcodeUsedAsync("111", null, CancellationToken.None)).Should().BeTrue();
        (await products.IsBarcodeUsedAsync("111", active.Id, CancellationToken.None)).Should().BeFalse("a product never conflicts with itself");
        (await products.IsBarcodeUsedAsync("222", null, CancellationToken.None)).Should().BeFalse("archived products free their barcode");
        (await products.IsBarcodeUsedAsync("333", null, CancellationToken.None)).Should().BeFalse("other shops are invisible");
        (await products.IsUnitBarcodeUsedAsync("U1", null, CancellationToken.None)).Should().BeTrue();
        (await products.IsUnitBarcodeUsedAsync("U1", unitId, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Repository_Find_LoadsUnitsTrackedAndHonoursTheTenant()
    {
        var shop = await fixture.CreateShopAsync();
        var otherShop = await fixture.CreateShopAsync("متجر آخر");
        var product = Catalog.NewProduct(shop.Id);
        product.AddUnit("علبة", 3m, 40m, null);
        await SaveAsync(shop, product);

        await using (var scope = fixture.CreateScope(shop.Id))
        {
            var products = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var found = await products.FindAsync(product.Id, CancellationToken.None);
            found!.Units.Should().HaveCount(2);
            found.RemoveUnit(found.Units.Single(unit => !unit.IsBase).Id).IsSuccess.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_units WHERE product_id = @p0", product.Id)).Should().Be(1);
        await using var otherScope = fixture.CreateScope(otherShop.Id);
        (await otherScope.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(product.Id, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task Database_RejectsASecondActiveProductWithTheSameBarcode()
    {
        var shop = await fixture.CreateShopAsync();
        await SaveAsync(shop, Product.Create(shop.Id, "a", "u", 1m, 1m, 0m, TestTime.T0, "999").Value);

        var act = () => SaveAsync(shop, Product.Create(shop.Id, "b", "u", 1m, 1m, 0m, TestTime.T0, "999").Value);

        (await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("ux_products_shop_id_barcode");
    }

    private async Task SaveAsync(Shop shop, params Product[] products)
    {
        await using var context = fixture.CreateContext(shop.Id);
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    private Task<Contracts.Common.PagedResponse<ProductListItem>> ListAsync(Shop shop, ProductListFilter filter) =>
        QueriesAsync(shop, queries => queries.ListAsync(filter, CancellationToken.None));

    private async Task<T> QueriesAsync<T>(Shop shop, Func<IProductQueries, Task<T>> read)
    {
        await using var scope = fixture.CreateScope(shop.Id);
        return await read(scope.ServiceProvider.GetRequiredService<IProductQueries>());
    }
}
