using Stockly.Domain.Shops;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Shops;

/// <summary>What a paid month buys (ADR-0015 §5) and the lapse of a paid period (daily downgrade job).</summary>
public sealed class ShopSubscriptionPaidMonthTests
{
    private static readonly DateOnly Today = new(2026, 10, 18);
    private static readonly Guid ProPlan = Guid.Parse("0192f0a0-0000-7000-8000-0000000000bb");

    [Fact]
    public void ExtendOneMonth_EndsInTheFuture_ExtendsFromTheCurrentEnd()
    {
        var subscription = Paid(endsOn: new DateOnly(2026, 11, 10));

        subscription.ExtendOneMonth(Today).IsSuccess.Should().BeTrue();

        subscription.EndsOn.Should().Be(new DateOnly(2026, 12, 10));
    }

    [Fact]
    public void ExtendOneMonth_EndedInThePast_ExtendsFromToday()
    {
        var subscription = Paid(endsOn: new DateOnly(2026, 10, 1), startsOn: new DateOnly(2026, 9, 1));

        subscription.ExtendOneMonth(Today).IsSuccess.Should().BeTrue();

        subscription.EndsOn.Should().Be(new DateOnly(2026, 11, 18));
    }

    [Fact]
    public void ExtendOneMonth_EndsToday_ExtendsFromToday()
    {
        var subscription = Paid(endsOn: Today);

        subscription.ExtendOneMonth(Today);

        subscription.EndsOn.Should().Be(new DateOnly(2026, 11, 18));
    }

    [Fact]
    public void ExtendOneMonth_OpenEnded_StaysOpenEnded()
    {
        var subscription = ShopSubscription.StartOpenEnded(ShopId, ProPlan, Today, Now).Value;

        subscription.ExtendOneMonth(Today).IsSuccess.Should().BeTrue();

        subscription.EndsOn.Should().BeNull();
    }

    [Fact]
    public void ExtendOneMonth_Ended_ReturnsNotActive()
    {
        var subscription = Paid(endsOn: Today);
        subscription.End(Today);

        subscription.ExtendOneMonth(Today).Error.Should().Be(ShopSubscriptionErrors.NotActive);
    }

    [Fact]
    public void StartPaidMonth_NewPlan_StartsTodayForOneMonthAndKeepsTheOverride()
    {
        var subscription = ShopSubscription.StartPaidMonth(ShopId, ProPlan, Today, 77, Now).Value;

        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.StartsOn.Should().Be(Today);
        subscription.EndsOn.Should().Be(new DateOnly(2026, 11, 18));
        subscription.DailyScanQuotaOverride.Should().Be(77);
    }

    [Fact]
    public void Lapse_LastDayBeforeToday_EndsAndKeepsTheLastDay()
    {
        var subscription = Paid(endsOn: Today.AddDays(-1), startsOn: Today.AddMonths(-1));

        subscription.Lapse(Today).IsSuccess.Should().BeTrue();

        subscription.Status.Should().Be(SubscriptionStatus.Ended);
        subscription.EndsOn.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public void Lapse_LastDayIsToday_ReturnsNotLapsed()
    {
        var subscription = Paid(endsOn: Today);

        subscription.Lapse(Today).Error.Should().Be(ShopSubscriptionErrors.NotLapsed);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void Lapse_OpenEnded_ReturnsNotLapsed()
    {
        var subscription = ShopSubscription.StartOpenEnded(ShopId, ProPlan, Today, Now).Value;

        subscription.Lapse(Today).Error.Should().Be(ShopSubscriptionErrors.NotLapsed);
    }

    [Fact]
    public void Lapse_Ended_ReturnsNotActive()
    {
        var subscription = Paid(endsOn: Today.AddDays(-1), startsOn: Today.AddMonths(-1));
        subscription.End(Today);

        subscription.Lapse(Today).Error.Should().Be(ShopSubscriptionErrors.NotActive);
    }

    private static ShopSubscription Paid(DateOnly endsOn, DateOnly? startsOn = null) =>
        ShopSubscription.Start(ShopId, ProPlan, startsOn ?? Today, endsOn, null, Now).Value;
}
