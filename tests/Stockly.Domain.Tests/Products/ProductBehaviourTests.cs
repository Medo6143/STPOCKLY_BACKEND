using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Products;

public sealed class ProductBehaviourTests
{
    [Fact]
    public void Rename_ValidName_UpdatesNameAndNormalizedName()
    {
        var product = CreateProduct();

        var result = product.Rename(" Panadol أكسترا ");

        result.IsSuccess.Should().BeTrue();
        product.Name.Should().Be("Panadol أكسترا");
        product.NormalizedName.Should().Be("panadol اكسترا");
    }

    [Fact]
    public void Rename_BlankAfterNormalization_ReturnsNameRequiredAndKeepsName()
    {
        var product = CreateProduct();

        var result = product.Rename(" ـ ");

        result.Error.Should().Be(ProductErrors.NameRequired);
        product.Name.Should().Be(ProductName);
    }

    [Fact]
    public void ChangeCostPrice_Valid_Updates()
    {
        var product = CreateProduct();

        product.ChangeCostPrice(9.75m).IsSuccess.Should().BeTrue();

        product.CostPrice.Should().Be(9.75m);
    }

    [Fact]
    public void ChangeCostPrice_TooManyDecimals_RejectsAndKeepsValue()
    {
        var product = CreateProduct(costPrice: 12.50m);

        var result = product.ChangeCostPrice(9.999m);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("costPrice", Precision.MoneyDecimals));
        product.CostPrice.Should().Be(12.50m);
    }

    [Fact]
    public void SetLowStockThreshold_Valid_Updates()
    {
        var product = CreateProduct();

        product.SetLowStockThreshold(2.5m).IsSuccess.Should().BeTrue();

        product.LowStockThreshold.Should().Be(2.5m);
    }

    [Fact]
    public void SetLowStockThreshold_Negative_RejectsAndKeepsValue()
    {
        var product = CreateProduct(lowStockThreshold: 5m);

        var result = product.SetLowStockThreshold(-1m);

        result.Error.Should().Be(PrecisionErrors.Negative("lowStockThreshold"));
        product.LowStockThreshold.Should().Be(5m);
    }

    [Fact]
    public void Archive_Twice_IsIdempotent()
    {
        var product = CreateProduct();

        product.Archive();
        product.Archive();

        product.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void EditMethods_ArchivedProduct_ReturnArchivedConflict()
    {
        var product = CreateProduct();
        product.Archive();

        product.Rename("اسم جديد").Error.Should().Be(ProductErrors.Archived);
        product.ChangeCostPrice(1m).Error.Should().Be(ProductErrors.Archived);
        product.SetLowStockThreshold(1m).Error.Should().Be(ProductErrors.Archived);
        product.AddUnit("علبة", 3m, 40m, null).Error.Should().Be(ProductErrors.Archived);
        product.UpdateUnit(product.BaseUnit.Id, "شريط", 1m, 16m, null).Error.Should().Be(ProductErrors.Archived);
        product.RemoveUnit(product.BaseUnit.Id).Error.Should().Be(ProductErrors.Archived);
        ProductErrors.Archived.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void RecordMovement_ArchivedProduct_IsStillAllowed()
    {
        var product = CreateProduct();
        product.Archive();

        var result = product.RecordMovement(-1m, MovementReason.ManualAdjustment, MovementSource.None, UserId, "expired, thrown away", Now);

        result.IsSuccess.Should().BeTrue();
        product.Quantity.Should().Be(-1m);
    }

    [Theory]
    [InlineData("0", "0", true)] // out of stock with the default threshold 0 is low stock
    [InlineData("-2", "0", true)] // negative stock is low stock
    [InlineData("5", "5", true)] // equal to threshold
    [InlineData("5.001", "5", false)]
    [InlineData("1", "0", false)]
    public void IsLowStock_QuantityAgainstThreshold_ReturnsExpected(string quantity, string threshold, bool expected)
    {
        var product = CreateProduct(lowStockThreshold: decimal.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture));
        var change = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture);
        if (change != 0m)
        {
            product.RecordMovement(change, MovementReason.ManualAdjustment, MovementSource.None, UserId, "setup", Now)
                .IsSuccess.Should().BeTrue();
        }

        product.IsLowStock.Should().Be(expected);
    }
}
