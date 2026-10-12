using Stockly.Domain.Common;
using Stockly.Domain.Products;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Products;

public sealed class ProductCreateTests
{
    [Fact]
    public void Create_ValidInput_ReturnsProductWithBaseUnit()
    {
        var result = Product.Create(ShopId, "  بَنادول  إكسترا ", " شريط ", 12.50m, 15m, 5m, Now);

        result.IsSuccess.Should().BeTrue();
        var product = result.Value;
        product.ShopId.Should().Be(ShopId);
        product.Name.Should().Be("بَنادول  إكسترا");
        product.NormalizedName.Should().Be("بنادول اكسترا");
        product.CostPrice.Should().Be(12.50m);
        product.LowStockThreshold.Should().Be(5m);
        product.Quantity.Should().Be(0m);
        product.IsArchived.Should().BeFalse();
        product.CreatedAt.Should().Be(Now);

        var baseUnit = product.Units.Should().ContainSingle().Subject;
        baseUnit.Should().BeSameAs(product.BaseUnit);
        baseUnit.IsBase.Should().BeTrue();
        baseUnit.Name.Should().Be("شريط");
        baseUnit.Factor.Should().Be(1m);
        baseUnit.SellPrice.Should().Be(15m);
        baseUnit.Barcode.Should().BeNull();
        baseUnit.ProductId.Should().Be(product.Id);
        baseUnit.ShopId.Should().Be(ShopId);
    }

    [Fact]
    public void Create_ZeroPricesAndThreshold_Succeeds()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, 0m, 0m, 0m, Now);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_EmptyShopId_ReturnsShopRequired()
    {
        var result = Product.Create(Guid.Empty, ProductName, BaseUnitName, 1m, 1m, 0m, Now);

        result.Error.Should().Be(ProductErrors.ShopRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("َِـ")] // only tashkeel and tatweel: empty after normalization
    public void Create_BlankName_ReturnsNameRequired(string name)
    {
        var result = Product.Create(ShopId, name, BaseUnitName, 1m, 1m, 0m, Now);

        result.Error.Should().Be(ProductErrors.NameRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankBaseUnitName_ReturnsUnitNameRequired(string baseUnitName)
    {
        var result = Product.Create(ShopId, ProductName, baseUnitName, 1m, 1m, 0m, Now);

        result.Error.Should().Be(ProductUnitErrors.NameRequired);
    }

    [Fact]
    public void Create_CostPriceWithThreeDecimals_IsRejectedNotRounded()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, 12.505m, 1m, 0m, Now);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("costPrice", Precision.MoneyDecimals));
    }

    [Fact]
    public void Create_NegativeCostPrice_ReturnsNegative()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, -1m, 1m, 0m, Now);

        result.Error.Should().Be(PrecisionErrors.Negative("costPrice"));
    }

    [Fact]
    public void Create_BaseUnitSellPriceWithThreeDecimals_IsRejected()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1.001m, 0m, Now);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("sellPrice", Precision.MoneyDecimals));
    }

    [Fact]
    public void Create_NegativeBaseUnitSellPrice_ReturnsNegative()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, 1m, -5m, 0m, Now);

        result.Error.Should().Be(PrecisionErrors.Negative("sellPrice"));
    }

    [Fact]
    public void Create_NegativeLowStockThreshold_ReturnsNegative()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1m, -1m, Now);

        result.Error.Should().Be(PrecisionErrors.Negative("lowStockThreshold"));
    }

    [Fact]
    public void Create_LowStockThresholdWithFourDecimals_IsRejected()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1m, 0.0001m, Now);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("lowStockThreshold", Precision.QuantityDecimals));
    }
}
