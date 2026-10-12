using Microsoft.EntityFrameworkCore;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class ConcurrencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SaveChanges_TwoContextsUpdateSameProduct_SecondGetsConcurrencyException()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        await using (var writer = fixture.CreateContext(shop.Id))
        {
            writer.Products.Add(product);
            await writer.SaveChangesAsync();
        }

        await using var first = fixture.CreateContext(shop.Id);
        await using var second = fixture.CreateContext(shop.Id);
        var firstCopy = await first.Products.SingleAsync(p => p.Id == product.Id);
        var secondCopy = await second.Products.SingleAsync(p => p.Id == product.Id);

        firstCopy.ChangeCostPrice(11m).IsSuccess.Should().BeTrue();
        await first.SaveChangesAsync();

        secondCopy.ChangeCostPrice(14m).IsSuccess.Should().BeTrue();
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
