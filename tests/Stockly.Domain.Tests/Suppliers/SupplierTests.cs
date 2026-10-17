using Stockly.Domain.Suppliers;

namespace Stockly.Domain.Tests.Suppliers;

public sealed class SupplierTests
{
    [Fact]
    public void Create_ValidName_TrimsAndNormalizes()
    {
        var supplier = Supplier.Create(TestData.ShopId, "  شركة الأدوية المتحدة ", " ", TestData.Now).Value;

        supplier.Name.Should().Be("شركة الأدوية المتحدة");
        supplier.NormalizedName.Should().Be("شركه الادويه المتحده");
        supplier.Phone.Should().BeNull();
        supplier.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Create_BlankName_Fails()
    {
        Supplier.Create(TestData.ShopId, " ـ ", null, TestData.Now).Error.Should().Be(SupplierErrors.NameRequired);
    }

    [Fact]
    public void Create_NoShop_Fails()
    {
        Supplier.Create(Guid.Empty, "x", null, TestData.Now).Error.Should().Be(SupplierErrors.ShopRequired);
    }

    [Fact]
    public void ProductAliasLearn_ValidText_IsConfirmedOnce()
    {
        var alias = ProductAlias.Learn(TestData.ShopId, Guid.CreateVersion7(), null, Guid.CreateVersion7(), " بنادول اكسترا ", TestData.Now).Value;

        alias.AliasText.Should().Be("بنادول اكسترا");
        alias.NormalizedAlias.Should().Be("بنادول اكسترا");
        alias.TimesConfirmed.Should().Be(1);
        alias.SupplierId.Should().BeNull();
        alias.LastUsedAt.Should().Be(TestData.Now);
    }

    [Fact]
    public void ProductAliasLearn_BlankText_Fails()
    {
        ProductAlias.Learn(TestData.ShopId, Guid.CreateVersion7(), null, Guid.CreateVersion7(), "  ", TestData.Now)
            .Error.Should().Be(SupplierErrors.AliasTextRequired);
    }
}
