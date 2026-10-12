using System.Globalization;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Products;

public sealed class ProductUnitTests
{
    [Fact]
    public void AddUnit_Valid_AddsNonBaseUnit()
    {
        var product = CreateProduct();

        var result = product.AddUnit(" علبة ", 3m, 40m, " 6221234567890 ");

        result.IsSuccess.Should().BeTrue();
        var unit = result.Value;
        unit.Name.Should().Be("علبة");
        unit.Factor.Should().Be(3m);
        unit.SellPrice.Should().Be(40m);
        unit.Barcode.Should().Be("6221234567890");
        unit.IsBase.Should().BeFalse();
        unit.ProductId.Should().Be(product.Id);
        unit.ShopId.Should().Be(ShopId);
        product.Units.Should().HaveCount(2).And.Contain(unit);
    }

    [Fact]
    public void AddUnit_BlankBarcode_StoresNull()
    {
        var product = CreateProduct();

        product.AddUnit("علبة", 3m, 40m, "  ").Value.Barcode.Should().BeNull();
    }

    [Fact]
    public void AddUnit_FractionalFactor_IsAllowed()
    {
        var product = CreateProduct(baseUnitName: "كيلو");

        var result = product.AddUnit("نصف كيلو", 0.5m, 20m, null);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("شريط")] // same as base unit
    [InlineData(" شَريـط ")] // same after normalization
    public void AddUnit_DuplicateNormalizedName_ReturnsDuplicateName(string name)
    {
        var product = CreateProduct();

        var result = product.AddUnit(name, 3m, 40m, null);

        result.Error.Should().Be(ProductUnitErrors.DuplicateName);
        product.Units.Should().ContainSingle();
    }

    [Fact]
    public void AddUnit_DuplicateBarcodeWithinProduct_ReturnsDuplicateBarcode()
    {
        var product = CreateProduct();
        product.AddUnit("علبة", 3m, 40m, "111").IsSuccess.Should().BeTrue();

        var result = product.AddUnit("كرتونة", 30m, 380m, "111");

        result.Error.Should().Be(ProductUnitErrors.DuplicateBarcode);
    }

    [Fact]
    public void AddUnit_BlankName_ReturnsNameRequired()
    {
        CreateProduct().AddUnit(" ", 3m, 40m, null).Error.Should().Be(ProductUnitErrors.NameRequired);
    }

    [Theory]
    [InlineData("0", "NotPositive")]
    [InlineData("-3", "NotPositive")]
    [InlineData("1.0000001", "TooManyDecimals")]
    [InlineData("100000000", "OutOfRange")]
    public void AddUnit_InvalidFactor_ReturnsPrecisionError(string factor, string expected)
    {
        var expectedError = expected switch
        {
            "NotPositive" => PrecisionErrors.NotPositive("factor"),
            "TooManyDecimals" => PrecisionErrors.TooManyDecimals("factor", Precision.FactorDecimals),
            _ => PrecisionErrors.OutOfRange("factor"),
        };

        var result = CreateProduct().AddUnit("علبة", decimal.Parse(factor, CultureInfo.InvariantCulture), 40m, null);

        result.Error.Should().Be(expectedError);
    }

    [Fact]
    public void AddUnit_SellPriceWithThreeDecimals_IsRejected()
    {
        CreateProduct().AddUnit("علبة", 3m, 40.005m, null).Error
            .Should().Be(PrecisionErrors.TooManyDecimals("sellPrice", Precision.MoneyDecimals));
    }

    [Fact]
    public void UpdateUnit_Valid_UpdatesFields()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        var result = product.UpdateUnit(box.Id, "علبة كبيرة", 6m, 75.5m, "222");

        result.IsSuccess.Should().BeTrue();
        box.Name.Should().Be("علبة كبيرة");
        box.Factor.Should().Be(6m);
        box.SellPrice.Should().Be(75.5m);
        box.Barcode.Should().Be("222");
    }

    [Fact]
    public void UpdateUnit_KeepOwnNameAndBarcode_IsNotADuplicate()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, "222").Value;

        product.UpdateUnit(box.Id, "علبة", 3m, 42m, "222").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void UpdateUnit_BaseUnitPriceAndName_CanChange()
    {
        var product = CreateProduct();

        var result = product.UpdateUnit(product.BaseUnit.Id, "شريط 10 أقراص", 1m, 16m, "333");

        result.IsSuccess.Should().BeTrue();
        product.BaseUnit.Name.Should().Be("شريط 10 أقراص");
        product.BaseUnit.SellPrice.Should().Be(16m);
        product.BaseUnit.IsBase.Should().BeTrue();
    }

    [Fact]
    public void UpdateUnit_BaseUnitFactor_CannotChange()
    {
        var product = CreateProduct();

        var result = product.UpdateUnit(product.BaseUnit.Id, BaseUnitName, 2m, 15m, null);

        result.Error.Should().Be(ProductUnitErrors.BaseUnitFactorFixed);
        product.BaseUnit.Factor.Should().Be(1m);
    }

    [Fact]
    public void UpdateUnit_NameOfAnotherUnit_ReturnsDuplicateName()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        var result = product.UpdateUnit(box.Id, "شَريط", 3m, 40m, null);

        result.Error.Should().Be(ProductUnitErrors.DuplicateName);
        box.Name.Should().Be("علبة");
    }

    [Fact]
    public void UpdateUnit_BarcodeOfAnotherUnit_ReturnsDuplicateBarcode()
    {
        var product = CreateProduct();
        product.UpdateUnit(product.BaseUnit.Id, BaseUnitName, 1m, 15m, "111").IsSuccess.Should().BeTrue();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        product.UpdateUnit(box.Id, "علبة", 3m, 40m, "111").Error.Should().Be(ProductUnitErrors.DuplicateBarcode);
    }

    [Fact]
    public void UpdateUnit_InvalidValues_ReturnsErrorAndKeepsUnit()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        product.UpdateUnit(box.Id, " ", 3m, 40m, null).Error.Should().Be(ProductUnitErrors.NameRequired);
        product.UpdateUnit(box.Id, "علبة", 0m, 40m, null).Error.Should().Be(PrecisionErrors.NotPositive("factor"));
        product.UpdateUnit(box.Id, "علبة", 3m, -1m, null).Error.Should().Be(PrecisionErrors.Negative("sellPrice"));
        box.Factor.Should().Be(3m);
        box.SellPrice.Should().Be(40m);
    }

    [Fact]
    public void UpdateUnit_UnknownUnit_ReturnsNotFound()
    {
        var result = CreateProduct().UpdateUnit(Guid.NewGuid(), "علبة", 3m, 40m, null);

        result.Error.Should().Be(ProductUnitErrors.NotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void RemoveUnit_NonBaseUnit_Removes()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        product.RemoveUnit(box.Id).IsSuccess.Should().BeTrue();

        product.Units.Should().ContainSingle().Which.IsBase.Should().BeTrue();
    }

    [Fact]
    public void RemoveUnit_BaseUnit_ReturnsBaseUnitCannotBeRemoved()
    {
        var product = CreateProduct();

        product.RemoveUnit(product.BaseUnit.Id).Error.Should().Be(ProductUnitErrors.BaseUnitCannotBeRemoved);
        product.Units.Should().ContainSingle();
    }

    [Fact]
    public void RemoveUnit_UnknownUnit_ReturnsNotFound()
    {
        CreateProduct().RemoveUnit(Guid.NewGuid()).Error.Should().Be(ProductUnitErrors.NotFound);
    }

    [Theory]
    [InlineData("3", "2", "6")] // 2 boxes of 3 strips
    [InlineData("1", "2.5", "2.5")] // base unit
    [InlineData("0.5", "3", "1.5")] // half-kilo bags
    [InlineData("0.333333", "1", "0.333")] // rounded to 3 decimals
    [InlineData("0.0005", "1", "0.001")] // midpoint away from zero (up)
    [InlineData("0.0005", "-1", "-0.001")] // midpoint away from zero (down)
    [InlineData("0.0025", "1", "0.003")] // banker's rounding (ToEven) would give 0.002
    [InlineData("24", "1.5", "36")]
    public void ToBase_QuantityInUnit_ConvertsAndRoundsAwayFromZero(string factor, string quantity, string expected)
    {
        var product = CreateProduct();
        var unit = product.AddUnit("وحدة", decimal.Parse(factor, CultureInfo.InvariantCulture), 1m, null).Value;

        unit.ToBase(decimal.Parse(quantity, CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToBase_BaseUnit_ReturnsSameQuantity()
    {
        CreateProduct().BaseUnit.ToBase(7.125m).Should().Be(7.125m);
    }
}
