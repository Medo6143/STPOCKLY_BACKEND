using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Domain.Tests.Shops;

public sealed class PlanTests
{
    [Fact]
    public void Create_ValidInput_ReturnsActivePlanWithTrimmedLowerCaseCode()
    {
        var plan = Plan.Create(" Premium ", " المميزة ", " Premium ", 100, 5, 199.99m).Value;

        plan.Id.Should().NotBe(Guid.Empty);
        plan.Code.Should().Be("premium");
        plan.NameAr.Should().Be("المميزة");
        plan.NameEn.Should().Be("Premium");
        plan.DailyScanQuota.Should().Be(100);
        plan.MaxMembers.Should().Be(5);
        plan.MonthlyPrice.Should().Be(199.99m);
        plan.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_DefaultPlanSeedValues_AreValid()
    {
        var plan = Plan.Create(
            DefaultPlan.Code, DefaultPlan.NameAr, DefaultPlan.NameEn, DefaultPlan.DailyScanQuota, DefaultPlan.MaxMembers, DefaultPlan.MonthlyPrice);

        plan.IsSuccess.Should().BeTrue("the seeded default plan must satisfy the same rules as any plan");
        plan.Value.Code.Should().Be(DefaultPlan.Code);
    }

    [Fact]
    public void DefaultPlan_SeedValues_MatchProductOwnerAssumptions()
    {
        DefaultPlan.Code.Should().Be("standard");
        DefaultPlan.NameAr.Should().Be("الأساسية");
        DefaultPlan.NameEn.Should().Be("Standard");
        DefaultPlan.DailyScanQuota.Should().Be(30);
        DefaultPlan.MaxMembers.Should().Be(3);
        DefaultPlan.MonthlyPrice.Should().Be(0.00m);
        DefaultPlan.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankCode_ReturnsCodeRequired(string code)
    {
        Plan.Create(code, "أ", "A", 1, 1, 0m).Error.Should().Be(PlanErrors.CodeRequired);
    }

    [Theory]
    [InlineData("", "Standard")]
    [InlineData("الأساسية", " ")]
    public void Create_BlankName_ReturnsNameRequired(string nameAr, string nameEn)
    {
        Plan.Create("standard", nameAr, nameEn, 1, 1, 0m).Error.Should().Be(PlanErrors.NameRequired);
    }

    [Fact]
    public void Create_NegativeQuota_ReturnsNegativeQuota()
    {
        Plan.Create("standard", "أ", "A", -1, 1, 0m).Error.Should().Be(PlanErrors.NegativeQuota);
    }

    [Fact]
    public void Create_ZeroMembers_ReturnsNoMembers()
    {
        Plan.Create("standard", "أ", "A", 1, 0, 0m).Error.Should().Be(PlanErrors.NoMembers);
    }

    [Theory]
    [InlineData("-1", "Precision.Negative")]
    [InlineData("1.001", "Precision.TooManyDecimals")]
    public void Create_InvalidPrice_ReturnsPrecisionError(string price, string code)
    {
        var result = Plan.Create("standard", "أ", "A", 1, 1, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture));

        result.Error.Code.Should().Be(code);
        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}
