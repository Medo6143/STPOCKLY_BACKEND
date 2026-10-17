using Microsoft.EntityFrameworkCore;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class TenantIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Query_AsShopA_SeesOnlyShopAProductsUnitsAndMovements()
    {
        var (shopA, productA) = await SeedShopWithStockAsync();
        var (_, productB) = await SeedShopWithStockAsync();
        Guid[] productIds = [productA.Id, productB.Id];

        await using var context = fixture.CreateContext(shopA);

        (await context.Products.Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync())
            .Should().Equal(productA.Id);
        (await context.Set<ProductUnit>().Where(u => productIds.Contains(u.ProductId)).Select(u => u.ProductId).Distinct().ToListAsync())
            .Should().Equal(productA.Id);
        (await context.StockMovements.Where(m => productIds.Contains(m.ProductId)).Select(m => m.ProductId).Distinct().ToListAsync())
            .Should().Equal(productA.Id);
    }

    [Fact]
    public async Task FindById_OtherShopsProduct_ReturnsNull()
    {
        var (shopA, _) = await SeedShopWithStockAsync();
        var (_, productB) = await SeedShopWithStockAsync();

        await using var context = fixture.CreateContext(shopA);

        (await context.Products.FirstOrDefaultAsync(p => p.Id == productB.Id)).Should().BeNull();
        (await context.Products.FindAsync(productB.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Include_Units_LoadsOnlyOwnUnits()
    {
        var (shopA, productA) = await SeedShopWithStockAsync();

        await using var context = fixture.CreateContext(shopA);
        var loaded = await context.Products.Include(p => p.Units).SingleAsync(p => p.Id == productA.Id);

        loaded.Units.Should().HaveCount(2).And.OnlyContain(u => u.ShopId == shopA);
    }

    [Fact]
    public async Task Query_WithoutTenant_ReturnsNoRowsAtAll()
    {
        await SeedShopWithStockAsync();

        await using var context = fixture.CreateContext(shopId: null);

        (await context.Products.CountAsync()).Should().Be(0);
        (await context.Set<ProductUnit>().CountAsync()).Should().Be(0);
        (await context.StockMovements.CountAsync()).Should().Be(0);
    }

    private async Task<(Guid ShopId, Product Product)> SeedShopWithStockAsync()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        product.AddUnit("علبة", 3m, 40m, null).IsSuccess.Should().BeTrue();
        var movement = Catalog.Receive(product, 30m, await fixture.CreateInvoiceLineAsync(shop.Id));

        await using var context = fixture.CreateContext(shop.Id);
        context.Products.Add(product);
        context.StockMovements.Add(movement);
        await context.SaveChangesAsync();

        movement.Reason.Should().Be(MovementReason.InvoiceReceipt);
        return (shop.Id, product);
    }
}
