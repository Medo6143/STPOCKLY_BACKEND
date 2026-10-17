using Stockly.Domain.Common;
using Stockly.Domain.Shops;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Shops;

/// <summary>The admin's changes to shops, subscriptions and plans (contract AD6–AD9, AD12, AD13).</summary>
public sealed class PlatformAdminShopTests
{
    private static readonly DateOnly Today = new(2026, 10, 18);

    [Fact]
    public void Create_Name_IsNormalizedForTheAdminSearch()
    {
        var shop = Shop.Create(" صَيدلية الشفاة ", ShopType.Pharmacy, null, null, null, Now).Value;

        shop.NormalizedName.Should().Be("صيدليه الشفاه");
    }

    [Fact]
    public void Suspend_ActiveShop_SuspendsWithTimeAndTrimmedReason()
    {
        var shop = NewShop();

        var changed = shop.Suspend("  unpaid  ", Now);

        changed.Should().BeTrue();
        shop.Status.Should().Be(ShopStatus.Suspended);
        shop.SuspendedAt.Should().Be(Now);
        shop.SuspendedReason.Should().Be("unpaid");
    }

    [Fact]
    public void Suspend_AlreadySuspended_ChangesNothing()
    {
        var shop = NewShop();
        shop.Suspend("first", Now);

        var changed = shop.Suspend("second", Now.AddHours(1));

        changed.Should().BeFalse();
        shop.SuspendedReason.Should().Be("first");
        shop.SuspendedAt.Should().Be(Now);
    }

    [Fact]
    public void Suspend_BlankReason_Throws()
    {
        var act = () => NewShop().Suspend(" ", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Activate_SuspendedShop_ClearsTheSuspension()
    {
        var shop = NewShop();
        shop.Suspend("unpaid", Now);

        var changed = shop.Activate();

        changed.Should().BeTrue();
        shop.Status.Should().Be(ShopStatus.Active);
        shop.SuspendedAt.Should().BeNull();
        shop.SuspendedReason.Should().BeNull();
    }

    [Fact]
    public void Activate_ActiveShop_ChangesNothing()
    {
        NewShop().Activate().Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(10_000)]
    public void SetQuotaOverride_InRange_SetsItAndReportsTheChange(int? value)
    {
        var subscription = NewSubscription();
        subscription.SetQuotaOverride(5);

        var result = subscription.SetQuotaOverride(value);

        result.Value.Should().BeTrue();
        subscription.DailyScanQuotaOverride.Should().Be(value);
    }

    [Fact]
    public void SetQuotaOverride_SameValue_ReportsNoChange()
    {
        var subscription = NewSubscription();
        subscription.SetQuotaOverride(50);

        subscription.SetQuotaOverride(50).Value.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void SetQuotaOverride_OutOfRange_FailsAndKeepsTheValue(int value)
    {
        var subscription = NewSubscription();

        var result = subscription.SetQuotaOverride(value);

        result.Error.Should().Be(ShopSubscriptionErrors.QuotaOverrideOutOfRange);
        subscription.DailyScanQuotaOverride.Should().BeNull();
    }

    [Fact]
    public void EffectiveDailyScanQuota_OverrideWinsOverThePlan()
    {
        var subscription = NewSubscription();
        subscription.EffectiveDailyScanQuota(30).Should().Be(30);

        subscription.SetQuotaOverride(0);

        subscription.EffectiveDailyScanQuota(30).Should().Be(0);
    }

    [Fact]
    public void End_Active_EndsToday()
    {
        var subscription = NewSubscription();

        subscription.End(Today).IsSuccess.Should().BeTrue();

        subscription.Status.Should().Be(SubscriptionStatus.Ended);
        subscription.EndsOn.Should().Be(Today);
        subscription.End(Today).Error.Should().Be(ShopSubscriptionErrors.NotActive);
    }

    [Fact]
    public void Start_WithEndAndOverride_IsActiveAndCarriesTheOverride()
    {
        var result = ShopSubscription.Start(ShopId, DefaultPlan.Id, Today, Today.AddDays(30), 70, Now);

        var subscription = result.Value;
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.StartsOn.Should().Be(Today);
        subscription.EndsOn.Should().Be(Today.AddDays(30));
        subscription.DailyScanQuotaOverride.Should().Be(70);
    }

    [Fact]
    public void Start_EndBeforeStart_Fails()
    {
        ShopSubscription.Start(ShopId, DefaultPlan.Id, Today, Today.AddDays(-1), null, Now).Error
            .Should().Be(ShopSubscriptionErrors.EndsBeforeStart);
    }

    [Fact]
    public void Start_OverrideOutOfRange_Fails()
    {
        ShopSubscription.Start(ShopId, DefaultPlan.Id, Today, null, 20_000, Now).Error
            .Should().Be(ShopSubscriptionErrors.QuotaOverrideOutOfRange);
    }

    [Fact]
    public void PlanCreate_Inactive_KeepsTheState()
    {
        var plan = Plan.Create("Pro-1", "برو", "Pro", 100, 5, 199.99m, isActive: false).Value;

        plan.Code.Should().Be("pro-1");
        plan.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PlanUpdate_ValidValues_ChangesEverythingButTheCode()
    {
        var plan = Plan.Create("pro", "برو", "Pro", 100, 5, 199.99m).Value;

        var result = plan.Update(" Pro+ ", " Pro+ ", 150, 8, 249.50m, isActive: false);

        result.IsSuccess.Should().BeTrue();
        plan.Code.Should().Be("pro");
        plan.NameAr.Should().Be("Pro+");
        plan.NameEn.Should().Be("Pro+");
        plan.DailyScanQuota.Should().Be(150);
        plan.MaxMembers.Should().Be(8);
        plan.MonthlyPrice.Should().Be(249.50m);
        plan.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("", 1, 1, "1.00", "Plan.NameRequired")]
    [InlineData("x", -1, 1, "1.00", "Plan.NegativeQuota")]
    [InlineData("x", 1, 0, "1.00", "Plan.NoMembers")]
    [InlineData("x", 1, 1, "1.001", "Precision.TooManyDecimals")]
    public void PlanUpdate_InvalidValue_FailsAndChangesNothing(string name, int quota, int members, string price, string code)
    {
        var plan = Plan.Create("pro", "برو", "Pro", 100, 5, 199.99m).Value;

        var result = plan.Update(name, name, quota, members, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), true);

        result.Error.Code.Should().Be(code);
        plan.NameEn.Should().Be("Pro");
        plan.DailyScanQuota.Should().Be(100);
    }

    [Fact]
    public void AdminErrors_HaveContractCodesAndTypes()
    {
        ShopErrors.NotFound.Should().Be(new Error("Shop.NotFound", ShopErrors.NotFound.Message, ErrorType.NotFound));
        PlanErrors.NotFound.Type.Should().Be(ErrorType.NotFound);
        PlanErrors.CodeTaken.Should().Match<Error>(error => error.Code == "Plan.CodeTaken" && error.Type == ErrorType.Conflict);
        PlanErrors.Inactive.Should().Match<Error>(error => error.Code == "Plan.Inactive" && error.Type == ErrorType.Validation);
    }

    private static Shop NewShop() => Shop.Create("صيدلية", ShopType.Pharmacy, null, null, null, Now).Value;

    private static ShopSubscription NewSubscription() =>
        ShopSubscription.StartOpenEnded(ShopId, DefaultPlan.Id, Today, Now).Value;
}
