using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class UnitOfWorkTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ExecuteInTransaction_ShouldCommitTrue_PersistsEveryWrite()
    {
        var shop = NewShop();

        await RunAsync(shop, commit: true);

        (await CountShopsAsync(shop)).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_ShouldCommitFalse_RollsBackWritesAlreadySaved()
    {
        var shop = NewShop();

        await RunAsync(shop, commit: false);

        (await CountShopsAsync(shop)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteInTransaction_Exception_RollsBack()
    {
        var shop = NewShop();

        var act = () => RunAsync(shop, commit: true, thenThrow: true);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await CountShopsAsync(shop)).Should().Be(0);
    }

    private static Shop NewShop() => Shop.Create($"shop-{Guid.NewGuid():N}", ShopType.Grocery, null, null, null, TestTime.T0).Value;

    private async Task RunAsync(Shop shop, bool commit, bool thenThrow = false)
    {
        await using var scope = fixture.CreateScope(shopId: null);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var shops = scope.ServiceProvider.GetRequiredService<IShopRepository>();

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                shops.Add(shop);
                await unitOfWork.SaveChangesAsync(token);
                return thenThrow ? throw new InvalidOperationException("boom") : commit;
            },
            shouldCommit => shouldCommit,
            CancellationToken.None);
    }

    private Task<long> CountShopsAsync(Shop shop) => fixture.ScalarAsync<long>("SELECT count(*) FROM shops WHERE id = @p0", shop.Id);
}
