using Stockly.Domain.Common;
using Stockly.Domain.Products;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Products;

/// <summary>Barcode and category (step 1.6) on create, and the all-or-nothing <see cref="Product.Update"/> (contract P4).</summary>
public sealed class ProductDetailsTests
{
    [Fact]
    public void Create_BarcodeAndCategory_AreTrimmed()
    {
        var product = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1m, 0m, Now, "  6221234567890 ", " مسكنات ").Value;

        product.Barcode.Should().Be("6221234567890");
        product.Category.Should().Be("مسكنات");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankBarcodeOrCategory_StoresNull(string? blank)
    {
        var product = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1m, 0m, Now, blank, blank).Value;

        product.Barcode.Should().BeNull();
        product.Category.Should().BeNull();
    }

    [Fact]
    public void Create_WithoutBarcodeOrCategory_StoresNull()
    {
        var product = CreateProduct();

        product.Barcode.Should().BeNull();
        product.Category.Should().BeNull();
    }

    [Fact]
    public void Create_BarcodeAtMaxLengthAfterTrim_IsAccepted()
    {
        var barcode = " " + new string('1', Product.BarcodeMaxLength) + " ";

        var result = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1m, 0m, Now, barcode);

        result.Value.Barcode.Should().HaveLength(Product.BarcodeMaxLength);
    }

    [Fact]
    public void Create_BarcodeTooLong_ReturnsBarcodeTooLong()
    {
        var result = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1m, 0m, Now, new string('1', Product.BarcodeMaxLength + 1));

        result.Error.Should().Be(ProductErrors.BarcodeTooLong);
    }

    [Fact]
    public void Create_CategoryTooLong_ReturnsCategoryTooLong()
    {
        var result = Product.Create(
            ShopId, ProductName, BaseUnitName, 1m, 1m, 0m, Now, category: new string('x', Product.CategoryMaxLength + 1));

        result.Error.Should().Be(ProductErrors.CategoryTooLong);
    }

    [Fact]
    public void Update_ValidValues_ReplacesEveryDetail()
    {
        var product = CreateProduct();

        var result = product.Update(" Panadol أكسترا ", " 111 ", " مسكنات ", 9.75m, 2.5m);

        result.IsSuccess.Should().BeTrue();
        product.Name.Should().Be("Panadol أكسترا");
        product.NormalizedName.Should().Be("panadol اكسترا");
        product.Barcode.Should().Be("111");
        product.Category.Should().Be("مسكنات");
        product.CostPrice.Should().Be(9.75m);
        product.LowStockThreshold.Should().Be(2.5m);
    }

    [Fact]
    public void Update_BlankBarcodeAndCategory_ClearsThem()
    {
        var product = Product.Create(ShopId, ProductName, BaseUnitName, 1m, 1m, 0m, Now, "111", "مسكنات").Value;

        product.Update(ProductName, " ", null, 1m, 0m).IsSuccess.Should().BeTrue();

        product.Barcode.Should().BeNull();
        product.Category.Should().BeNull();
    }

    [Fact]
    public void Update_OneInvalidValue_ChangesNothing()
    {
        var product = Product.Create(ShopId, ProductName, BaseUnitName, 12.50m, 15m, 5m, Now, "111", "مسكنات").Value;

        var result = product.Update("New name", "222", "other", 9.75m, 0.0001m);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("lowStockThreshold", Precision.QuantityDecimals));
        product.Name.Should().Be(ProductName);
        product.Barcode.Should().Be("111");
        product.Category.Should().Be("مسكنات");
        product.CostPrice.Should().Be(12.50m);
        product.LowStockThreshold.Should().Be(5m);
    }

    [Theory]
    [InlineData(" ـ ", "111", 1, 0, "Product.NameRequired")]
    [InlineData("name", "111", -1, 0, "Precision.Negative")]
    [InlineData("name", "111", 1.001, 0, "Precision.TooManyDecimals")]
    [InlineData("name", "111", 1, -1, "Precision.Negative")]
    public void Update_InvalidValue_ReturnsItsError(string name, string barcode, decimal costPrice, decimal threshold, string code)
    {
        var product = CreateProduct();

        product.Update(name, barcode, null, costPrice, threshold).Error.Code.Should().Be(code);
    }

    [Fact]
    public void Update_BarcodeOrCategoryTooLong_ReturnsTheirErrors()
    {
        var product = CreateProduct();

        product.Update(ProductName, new string('1', Product.BarcodeMaxLength + 1), null, 1m, 0m)
            .Error.Should().Be(ProductErrors.BarcodeTooLong);
        product.Update(ProductName, null, new string('x', Product.CategoryMaxLength + 1), 1m, 0m)
            .Error.Should().Be(ProductErrors.CategoryTooLong);
    }

    [Fact]
    public void Update_ArchivedProduct_ReturnsArchivedBeforeValueErrors()
    {
        var product = CreateProduct();
        product.Archive();

        product.Update(" ", null, null, -1m, 0m).Error.Should().Be(ProductErrors.Archived);
    }

    [Fact]
    public void FindUnit_Null_ReturnsBaseUnit()
    {
        var product = CreateProduct();

        product.FindUnit(null).Value.Should().Be(product.BaseUnit);
    }

    [Fact]
    public void FindUnit_UnitOfTheProduct_ReturnsIt()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        product.FindUnit(box.Id).Value.Should().Be(box);
    }

    [Fact]
    public void FindUnit_UnknownId_ReturnsUnitNotFound()
    {
        var product = CreateProduct();

        product.FindUnit(Guid.NewGuid()).Error.Should().Be(ProductUnitErrors.NotFound);
    }
}
