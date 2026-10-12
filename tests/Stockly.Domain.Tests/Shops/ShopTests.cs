using Stockly.Domain.Shops;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Shops;

public sealed class ShopTests
{
    [Fact]
    public void Create_ValidInput_ReturnsActiveShop()
    {
        var result = Shop.Create(" صيدلية الشفاء ", ShopType.Pharmacy, " 01001234567 ", " القاهرة ", " 12 شارع النيل ", Now);

        result.IsSuccess.Should().BeTrue();
        var shop = result.Value;
        shop.Id.Should().NotBe(Guid.Empty);
        shop.Name.Should().Be("صيدلية الشفاء");
        shop.ShopType.Should().Be(ShopType.Pharmacy);
        shop.Phone.Should().Be("01001234567");
        shop.Governorate.Should().Be("القاهرة");
        shop.Address.Should().Be("12 شارع النيل");
        shop.Status.Should().Be(ShopStatus.Active);
        shop.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_BlankOptionalFields_StoresNull()
    {
        var shop = Shop.Create("بقالة", ShopType.Grocery, " ", null, "", Now).Value;

        shop.Phone.Should().BeNull();
        shop.Governorate.Should().BeNull();
        shop.Address.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("َـ")] // only tashkeel and tatweel
    public void Create_BlankName_ReturnsNameRequired(string name)
    {
        Shop.Create(name, ShopType.Other, null, null, null, Now).Error.Should().Be(ShopErrors.NameRequired);
    }

    [Fact]
    public void Create_UndefinedType_ReturnsUnknownType()
    {
        Shop.Create("محل", (ShopType)0, null, null, null, Now).Error.Should().Be(ShopErrors.UnknownType);
    }

    [Fact]
    public void Enums_Values_AreExplicitAndStable()
    {
        ((int)ShopType.Pharmacy).Should().Be(1);
        ((int)ShopType.Grocery).Should().Be(2);
        ((int)ShopType.MobileShop).Should().Be(3);
        ((int)ShopType.Other).Should().Be(4);
        ((int)ShopStatus.Active).Should().Be(1);
        ((int)ShopStatus.Suspended).Should().Be(2);
    }
}
