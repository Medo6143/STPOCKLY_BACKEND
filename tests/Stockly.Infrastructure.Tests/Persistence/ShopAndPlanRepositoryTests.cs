using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class ShopAndPlanRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetStatus_ExistingActiveSuspendedAndUnknownShop()
    {
        var active = await fixture.CreateShopAsync();
        var suspended = await fixture.CreateShopAsync();
        await fixture.ExecuteAsync("UPDATE shops SET status = 2 WHERE id = @p0", suspended.Id);

        await using var scope = fixture.CreateScope(shopId: null);
        var shops = scope.ServiceProvider.GetRequiredService<IShopRepository>();

        (await shops.GetStatusAsync(active.Id, CancellationToken.None)).Should().Be(ShopStatus.Active);
        (await shops.GetStatusAsync(suspended.Id, CancellationToken.None)).Should().Be(ShopStatus.Suspended);
        (await shops.GetStatusAsync(Guid.CreateVersion7(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task FindActiveIdByCode_DefaultPlan_ReturnsSeededId_UnknownCode_Null()
    {
        await using var scope = fixture.CreateScope(shopId: null);
        var plans = scope.ServiceProvider.GetRequiredService<IPlanRepository>();

        (await plans.FindActiveIdByCodeAsync(DefaultPlan.Code, CancellationToken.None)).Should().Be(DefaultPlan.Id);
        (await plans.FindActiveIdByCodeAsync("no-such-plan", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ShopSubscriptions_AreTenantFiltered()
    {
        var shopA = await fixture.CreateShopAsync();
        var shopB = await fixture.CreateShopAsync();
        foreach (var shop in new[] { shopA, shopB })
        {
            await using var writer = fixture.CreateScope(shop.Id);
            writer.ServiceProvider.GetRequiredService<IShopSubscriptionRepository>()
                .Add(ShopSubscription.StartOpenEnded(shop.Id, DefaultPlan.Id, new DateOnly(2026, 10, 12), TestTime.T0).Value);
            await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var reader = fixture.CreateScope(shopA.Id);
        var visible = await reader.ServiceProvider.GetRequiredService<StocklyDbContext>().Set<ShopSubscription>()
            .Where(s => s.ShopId == shopA.Id || s.ShopId == shopB.Id)
            .Select(s => s.ShopId)
            .ToListAsync();

        visible.Should().Equal(shopA.Id);
    }

    [Fact]
    public async Task ShopSubscription_WithoutTenant_CannotBeInserted()
    {
        var shop = await fixture.CreateShopAsync();

        await using var scope = fixture.CreateScope(shopId: null);
        scope.ServiceProvider.GetRequiredService<IShopSubscriptionRepository>()
            .Add(ShopSubscription.StartOpenEnded(shop.Id, DefaultPlan.Id, new DateOnly(2026, 10, 12), TestTime.T0).Value);
        var act = () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<TenantViolationException>("registration must open a tenant scope first");
    }
}
