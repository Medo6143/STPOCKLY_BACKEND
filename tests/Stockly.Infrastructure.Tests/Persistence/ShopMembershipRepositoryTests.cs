using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// ADR-0002 update (2026-10-12): <c>shop_members</c> has no tenant filter, so every read must be filtered by user id.
/// Two users in two shops; every method returns only the given user's rows.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ShopMembershipRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task FindFirstActive_TwoUsersTwoShops_ReturnsOnlyTheGivenUsersMembership()
    {
        var (userA, shopA, userB, shopB) = await TwoUsersInTwoShopsAsync();

        await using var scope = fixture.CreateScope(shopId: null);
        var repository = scope.ServiceProvider.GetRequiredService<IShopMembershipRepository>();

        var forA = await repository.FindFirstActiveAsync(userA, CancellationToken.None);
        var forB = await repository.FindFirstActiveAsync(userB, CancellationToken.None);

        forA!.UserId.Should().Be(userA);
        forA.ShopId.Should().Be(shopA.Id);
        forA.ShopName.Should().Be(shopA.Name);
        forA.Role.Should().Be(ShopRole.Owner);
        forA.ShopStatus.Should().Be(ShopStatus.Active);
        forB!.UserId.Should().Be(userB);
        forB.ShopId.Should().Be(shopB.Id);
    }

    [Fact]
    public async Task FindActive_OtherUsersShop_ReturnsNull_OwnShop_ReturnsIt()
    {
        var (userA, shopA, userB, shopB) = await TwoUsersInTwoShopsAsync();

        await using var scope = fixture.CreateScope(shopId: null);
        var repository = scope.ServiceProvider.GetRequiredService<IShopMembershipRepository>();

        (await repository.FindActiveAsync(userA, shopB.Id, CancellationToken.None)).Should().BeNull("user A is not a member of shop B");
        (await repository.FindActiveAsync(userB, shopA.Id, CancellationToken.None)).Should().BeNull("user B is not a member of shop A");
        (await repository.FindActiveAsync(userA, shopA.Id, CancellationToken.None))!.UserId.Should().Be(userA);
        (await repository.FindActiveAsync(userB, shopB.Id, CancellationToken.None))!.UserId.Should().Be(userB);
    }

    [Fact]
    public async Task FindFirstActive_SeveralMemberships_ReturnsTheActiveOneJoinedFirst()
    {
        var user = await fixture.CreateUserAsync();
        var inactiveOldest = await fixture.CreateShopAsync();
        var activeOlder = await fixture.CreateShopAsync();
        var activeNewer = await fixture.CreateShopAsync();
        await InsertMemberAsync(inactiveOldest.Id, user, ShopRole.Staff, isActive: false, TestTime.T0);
        await InsertMemberAsync(activeNewer.Id, user, ShopRole.Owner, isActive: true, TestTime.T2);
        await InsertMemberAsync(activeOlder.Id, user, ShopRole.Staff, isActive: true, TestTime.T1);

        await using var scope = fixture.CreateScope(shopId: null);
        var membership = await scope.ServiceProvider.GetRequiredService<IShopMembershipRepository>()
            .FindFirstActiveAsync(user, CancellationToken.None);

        membership!.ShopId.Should().Be(activeOlder.Id);
        membership.Role.Should().Be(ShopRole.Staff);
        membership.JoinedAt.Should().Be(TestTime.T1);
    }

    [Fact]
    public async Task FindActive_InactiveMembership_ReturnsNull()
    {
        var user = await fixture.CreateUserAsync();
        var shop = await fixture.CreateShopAsync();
        await InsertMemberAsync(shop.Id, user, ShopRole.Staff, isActive: false, TestTime.T0);

        await using var scope = fixture.CreateScope(shopId: null);
        var repository = scope.ServiceProvider.GetRequiredService<IShopMembershipRepository>();

        (await repository.FindActiveAsync(user, shop.Id, CancellationToken.None)).Should().BeNull();
        (await repository.FindFirstActiveAsync(user, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Add_SavesTheMembership_EvenWithAnotherTenantInScope()
    {
        var user = await fixture.CreateUserAsync();
        var shop = await fixture.CreateShopAsync();
        var member = ShopMember.Create(shop.Id, user, ShopRole.Owner, TestTime.T0).Value;

        await using (var scope = fixture.CreateScope(shopId: null))
        {
            scope.ServiceProvider.GetRequiredService<IShopMembershipRepository>().Add(member);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM shop_members WHERE shop_id = @p0 AND user_id = @p1 AND is_active", shop.Id, user))
            .Should().Be(1);
    }

    private async Task<(Guid UserA, Shop ShopA, Guid UserB, Shop ShopB)> TwoUsersInTwoShopsAsync()
    {
        var userA = await fixture.CreateUserAsync();
        var userB = await fixture.CreateUserAsync();
        var shopA = await fixture.CreateShopAsync("صيدلية أ");
        var shopB = await fixture.CreateShopAsync("صيدلية ب");
        await InsertMemberAsync(shopA.Id, userA, ShopRole.Owner, isActive: true, TestTime.T1);
        await InsertMemberAsync(shopB.Id, userB, ShopRole.Owner, isActive: true, TestTime.T0);
        return (userA, shopA, userB, shopB);
    }

    private Task<int> InsertMemberAsync(Guid shopId, Guid userId, ShopRole role, bool isActive, DateTimeOffset joinedAt) =>
        fixture.ExecuteAsync(
            "INSERT INTO shop_members (shop_id, user_id, role, is_active, joined_at) VALUES (@p0, @p1, @p2, @p3, @p4)",
            shopId,
            userId,
            (short)role,
            isActive,
            joinedAt);
}
