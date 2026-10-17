using Stockly.Domain.Common;
using Stockly.Domain.Sales;
using Stockly.Domain.Suppliers;

namespace Stockly.Domain.Tests.Sales;

/// <summary><see cref="Sale"/> and <see cref="SaleItem"/> (contract S1, erd §3.4).</summary>
public sealed class SaleTests
{
    [Fact]
    public void Start_Valid_HeaderWithZeroTotalAndTrimmedNote()
    {
        var sale = Start(note: "  نقدي  ").Value;

        sale.TotalAmount.Should().Be(0m);
        sale.Note.Should().Be("نقدي");
        sale.Items.Should().BeEmpty();
        Start(note: "   ").Value.Note.Should().BeNull();
    }

    [Fact]
    public void Start_InvalidHeader_Refused()
    {
        Sale.Start(Guid.Empty, Guid.CreateVersion7(), TestData.UserId, null, TestData.Now).Error.Should().Be(SaleErrors.ShopRequired);
        Sale.Start(TestData.ShopId, Guid.Empty, TestData.UserId, null, TestData.Now).Error.Should().Be(SaleErrors.ClientRequestIdRequired);
        Sale.Start(TestData.ShopId, Guid.CreateVersion7(), Guid.Empty, null, TestData.Now).Error.Should().Be(SaleErrors.UserRequired);
        Start(note: new string('x', SaleLimits.NoteMaxLength + 1)).Error.Should().Be(SaleErrors.NoteTooLong);
    }

    [Fact]
    public void AddItem_DefaultPrice_ChargesTheListPrice()
    {
        var sale = Start().Value;
        var product = TestData.CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        var item = sale.AddItem(product, box, 2m, unitPrice: null).Value;

        item.ListUnitPrice.Should().Be(40m);
        item.UnitPrice.Should().Be(40m);
        item.PriceOverridden.Should().BeFalse();
        item.LineTotal.Should().Be(80m);
        item.LineNo.Should().Be(1);
        sale.TotalAmount.Should().Be(80m);
    }

    [Fact]
    public void AddItem_OverriddenPrice_KeepsBothPricesAndRoundsTheLineAwayFromZero()
    {
        var sale = Start().Value;
        var product = TestData.CreateProduct();

        var item = sale.AddItem(product, product.BaseUnit, 1.5m, 14.25m).Value;
        var second = sale.AddItem(product, product.BaseUnit, 1m, 15m).Value;

        item.ListUnitPrice.Should().Be(15m);
        item.UnitPrice.Should().Be(14.25m);
        item.PriceOverridden.Should().BeTrue();
        item.LineTotal.Should().Be(21.38m, "21.375 rounds away from zero");
        second.LineNo.Should().Be(2);
        sale.TotalAmount.Should().Be(36.38m);
    }

    [Fact]
    public void AddItem_ArchivedProduct_RefusedAndNothingChanges()
    {
        var sale = Start().Value;
        var product = TestData.CreateProduct();
        product.Archive();

        sale.AddItem(product, product.BaseUnit, 1m, null).Error.Should().Be(SaleErrors.ArchivedProduct);
        sale.Items.Should().BeEmpty();
        sale.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public void AddItem_UnitOfAnotherProduct_Refused()
    {
        var sale = Start().Value;

        sale.AddItem(TestData.CreateProduct(), TestData.CreateProduct("آخر").BaseUnit, 1m, null).Error.Should().Be(SaleErrors.UnitNotOfProduct);
    }

    [Theory]
    [InlineData("0", "Sale.QuantityNotPositive")]
    [InlineData("-1", "Sale.QuantityNotPositive")]
    [InlineData("1.0005", "Precision.TooManyDecimals")]
    public void AddItem_BadQuantity_Refused(string quantity, string code)
    {
        var product = TestData.CreateProduct();

        Start().Value.AddItem(product, product.BaseUnit, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), null)
            .Error.Code.Should().Be(code);
    }

    [Fact]
    public void AddItem_NegativeOrTooPrecisePrice_Refused()
    {
        var product = TestData.CreateProduct();

        Start().Value.AddItem(product, product.BaseUnit, 1m, -1m).Error.Code.Should().Be("Precision.Negative");
        Start().Value.AddItem(product, product.BaseUnit, 1m, 1.001m).Error.Code.Should().Be("Precision.TooManyDecimals");
    }

    [Fact]
    public void AddItem_MoreThanMaxItems_TooManyItems()
    {
        var sale = Start().Value;
        var product = TestData.CreateProduct();
        for (var index = 0; index < SaleLimits.MaxItems; index++)
        {
            sale.AddItem(product, product.BaseUnit, 1m, null).IsSuccess.Should().BeTrue();
        }

        sale.AddItem(product, product.BaseUnit, 1m, null).Error.Should().Be(SaleErrors.TooManyItems);
        sale.Items.Should().HaveCount(SaleLimits.MaxItems);
    }

    [Fact]
    public void ComputeLineTotal_RoundsToTwoDecimalsAwayFromZero()
    {
        SaleItem.ComputeLineTotal(0.005m, 1m).Should().Be(0.01m);
        SaleItem.ComputeLineTotal(3.333m, 3.33m).Should().Be(11.10m);
    }

    [Fact]
    public void ProductAliasLearn_TimesConfirmed_AtLeastOne()
    {
        var product = TestData.CreateProduct();

        ProductAlias.Learn(TestData.ShopId, product.Id, null, product.BaseUnit.Id, "اسم", TestData.Now, 3).Value.TimesConfirmed.Should().Be(3);
        ProductAlias.Learn(TestData.ShopId, product.Id, null, product.BaseUnit.Id, "اسم", TestData.Now, 0).Error
            .Should().Be(SupplierErrors.TimesConfirmedNotPositive);
    }

    private static Result<Sale> Start(string? note = null) =>
        Sale.Start(TestData.ShopId, Guid.CreateVersion7(), TestData.UserId, note, TestData.Now);
}
