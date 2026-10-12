using System.Globalization;
using Stockly.Application.Features.Products;
using Stockly.Domain.Stock;
using static Stockly.Application.Tests.Support.ProductTestContext;
using ContractMovementReason = Stockly.Contracts.Products.MovementReason;

namespace Stockly.Application.Tests.Features.Products;

public sealed class ProductMappingTests
{
    [Theory]
    [InlineData(12.5, "12.50")]
    [InlineData(0, "0.00")]
    public void Money_HasExactlyTwoDecimals(decimal value, string expected)
    {
        ProductMapping.Money(value).Should().Be(expected);
    }

    [Fact]
    public void Money_LargestValue_IsNotRounded()
    {
        ProductMapping.Money(9_999_999_999_999_999.99m).Should().Be("9999999999999999.99");
    }

    [Theory]
    [InlineData(1.5, "1.500")]
    [InlineData(-2, "-2.000")]
    [InlineData(0, "0.000")]
    public void Quantity_HasExactlyThreeDecimals(decimal value, string expected)
    {
        ProductMapping.Quantity(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1.000000")]
    [InlineData(0.333333, "0.333333")]
    public void Factor_HasExactlySixDecimals(decimal value, string expected)
    {
        ProductMapping.Factor(value).Should().Be(expected);
    }

    [Fact]
    public void Formats_IgnoreTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-EG");
        try
        {
            ProductMapping.Money(1234.5m).Should().Be("1234.50");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ContractMovementReason_MatchesDomainByNameAndValue()
    {
        Pairs<ContractMovementReason>().Should().Equal(Pairs<MovementReason>());
    }

    [Fact]
    public void ToDto_Unit_FormatsFactorAndPrice()
    {
        var product = NewProduct();
        var box = product.AddUnit("علبة", 3m, 40m, " 111 ").Value;

        var dto = box.ToDto();

        dto.Should().BeEquivalentTo(new { box.Id, Name = "علبة", Factor = "3.000000", SellPrice = "40.00", Barcode = "111", IsBase = false });
    }

    [Fact]
    public void ToDto_Movement_MapsEveryFieldWithUtcTime()
    {
        var product = NewProduct();
        var movement = product.AdjustStock(null, -2m, UserId, "Expired", Now.ToOffset(TimeSpan.FromHours(3))).Value;

        var dto = movement.ToDto();

        dto.QuantityChange.Should().Be("-2.000");
        dto.BalanceAfter.Should().Be("-2.000");
        dto.Reason.Should().Be(ContractMovementReason.ManualAdjustment);
        dto.Note.Should().Be("Expired");
        dto.ProductId.Should().Be(product.Id);
        dto.CreatedByUserId.Should().Be(UserId);
        dto.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        dto.CreatedAt.Should().Be(Now.UtcDateTime);
        dto.InvoiceId.Should().BeNull();
        dto.SaleId.Should().BeNull();
    }

    private static List<(string, int)> Pairs<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => (value.ToString(), Convert.ToInt32(value, CultureInfo.InvariantCulture))).ToList();
}
