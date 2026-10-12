using Stockly.Domain.Common;
using Stockly.Domain.Shops;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Shops;

public sealed class ShopSubscriptionTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    [Fact]
    public void StartOpenEnded_ValidInput_ReturnsActiveOpenEndedTenantSubscription()
    {
        var subscription = ShopSubscription.StartOpenEnded(ShopId, DefaultPlan.Id, Today, Now).Value;

        subscription.Should().BeAssignableTo<ITenantEntity>();
        subscription.Id.Should().NotBe(Guid.Empty);
        subscription.ShopId.Should().Be(ShopId);
        subscription.PlanId.Should().Be(DefaultPlan.Id);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.StartsOn.Should().Be(Today);
        subscription.EndsOn.Should().BeNull();
        subscription.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void StartOpenEnded_EmptyShop_ReturnsShopRequired()
    {
        ShopSubscription.StartOpenEnded(Guid.Empty, DefaultPlan.Id, Today, Now).Error.Should().Be(ShopSubscriptionErrors.ShopRequired);
    }

    [Fact]
    public void StartOpenEnded_EmptyPlan_ReturnsPlanRequired()
    {
        ShopSubscription.StartOpenEnded(ShopId, Guid.Empty, Today, Now).Error.Should().Be(ShopSubscriptionErrors.PlanRequired);
    }

    [Fact]
    public void SubscriptionStatus_Values_AreExplicitAndStable()
    {
        ((int)SubscriptionStatus.Active).Should().Be(1);
        ((int)SubscriptionStatus.Ended).Should().Be(2);
    }
}
