using Microsoft.EntityFrameworkCore;
using Stockly.Domain.Products;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class InterceptorTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Insert_OtherShopsShopId_ThrowsAndSavesNothing()
    {
        var shopA = await fixture.CreateShopAsync();
        var shopB = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shopB.Id);

        await using (var context = fixture.CreateContext(shopA.Id))
        {
            context.Products.Add(product);
            var act = () => context.SaveChangesAsync();
            await act.Should().ThrowAsync<TenantViolationException>();
        }

        await using var asShopB = fixture.CreateContext(shopB.Id);
        (await asShopB.Products.AnyAsync(p => p.Id == product.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Insert_WithoutTenant_Throws()
    {
        var shop = await fixture.CreateShopAsync();

        await using var context = fixture.CreateContext(shopId: null);
        context.Products.Add(Catalog.NewProduct(shop.Id));
        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<TenantViolationException>();
    }

    [Fact]
    public async Task Insert_EmptyShopId_IsStampedFromCurrentTenant()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);

        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Products.Add(product);
            context.Entry(product).Property(p => p.ShopId).CurrentValue = Guid.Empty;
            await context.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext(shop.Id);
        (await reader.Products.SingleAsync(p => p.Id == product.Id)).ShopId.Should().Be(shop.Id);
    }

    [Fact]
    public async Task Update_ChangedShopId_Throws()
    {
        var shopA = await fixture.CreateShopAsync();
        var shopB = await fixture.CreateShopAsync();
        var product = await SaveProductAsync(shopA.Id);

        await using var context = fixture.CreateContext(shopA.Id);
        var loaded = await context.Products.SingleAsync(p => p.Id == product.Id);
        context.Entry(loaded).Property(p => p.ShopId).CurrentValue = shopB.Id;
        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<TenantViolationException>();
    }

    [Fact]
    public async Task SaveChanges_Sync_AlsoEnforcesTenant()
    {
        var shopA = await fixture.CreateShopAsync();
        var shopB = await fixture.CreateShopAsync();

        await using var context = fixture.CreateContext(shopA.Id);
        context.Products.Add(Catalog.NewProduct(shopB.Id));
        var act = () => context.SaveChanges();

        act.Should().Throw<TenantViolationException>();
    }

    [Fact]
    public async Task UpdatedAt_InsertThenUpdate_SetFromClock()
    {
        var shop = await fixture.CreateShopAsync();
        var clock = new FakeClock(TestTime.T1);
        var product = Catalog.NewProduct(shop.Id);

        await using (var context = fixture.CreateContext(shop.Id, clock))
        {
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        (await ReadUpdatedAtAsync(shop.Id, product.Id)).Should().Be(TestTime.T1);

        clock.UtcNow = TestTime.T2;
        await using (var context = fixture.CreateContext(shop.Id, clock))
        {
            var loaded = await context.Products.SingleAsync(p => p.Id == product.Id);
            loaded.ChangeCostPrice(13m).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        }

        (await ReadUpdatedAtAsync(shop.Id, product.Id)).Should().Be(TestTime.T2);
    }

    private async Task<Product> SaveProductAsync(Guid shopId)
    {
        var product = Catalog.NewProduct(shopId);
        await using var context = fixture.CreateContext(shopId);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private async Task<DateTimeOffset> ReadUpdatedAtAsync(Guid shopId, Guid productId)
    {
        await using var context = fixture.CreateContext(shopId);
        return await context.Products
            .Where(p => p.Id == productId)
            .Select(p => EF.Property<DateTimeOffset>(p, ShadowProperties.UpdatedAt))
            .SingleAsync();
    }
}
