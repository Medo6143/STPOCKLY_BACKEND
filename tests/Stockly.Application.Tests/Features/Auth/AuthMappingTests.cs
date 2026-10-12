using NSubstitute;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Users;
using ContractShopRole = Stockly.Contracts.Shops.ShopRole;
using ContractShopStatus = Stockly.Contracts.Shops.ShopStatus;
using ContractShopType = Stockly.Contracts.Shops.ShopType;
using DomainShopRole = Stockly.Domain.Shops.ShopRole;
using DomainShopStatus = Stockly.Domain.Shops.ShopStatus;
using DomainShopType = Stockly.Domain.Shops.ShopType;

namespace Stockly.Application.Tests.Features.Auth;

public sealed class AuthMappingTests
{
    [Fact]
    public void ContractEnums_MatchDomainEnumsByNameAndValue()
    {
        Pairs<DomainShopType>().Should().Equal(Pairs<ContractShopType>());
        Pairs<DomainShopStatus>().Should().Equal(Pairs<ContractShopStatus>());
        Pairs<DomainShopRole>().Should().Equal(Pairs<ContractShopRole>());
    }

    [Theory]
    [InlineData(null, PreferredLanguage.Ar)]
    [InlineData("ar", PreferredLanguage.Ar)]
    [InlineData("en", PreferredLanguage.En)]
    public void ToPreferredLanguage_MapsContractCodes(string? code, PreferredLanguage expected)
    {
        AuthMapping.ToPreferredLanguage(code).Should().Be(expected);
        AuthMapping.ToCode(expected).Should().Be(code ?? "ar");
    }

    [Theory]
    [InlineData("Pharmacy", true)]
    [InlineData("MobileShop", true)]
    [InlineData("mobileshop", false)]
    [InlineData("3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseShopType_AcceptsOnlyExactNames(string? value, bool accepted)
    {
        AuthMapping.TryParseShopType(value, out _).Should().Be(accepted);
    }

    [Theory]
    [InlineData("2026-10-12T12:00:00Z", "2026-10-12")]
    [InlineData("2026-10-12T23:30:00Z", "2026-10-13")]
    [InlineData("2026-01-15T21:59:00Z", "2026-01-15")]
    [InlineData("2026-01-15T22:01:00Z", "2026-01-16")]
    public void BusinessCalendarToday_IsTheCairoDate(string utc, string cairoDate)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        clock.Today().Should().Be(DateOnly.Parse(cairoDate, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static List<(string, int)> Pairs<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => (value.ToString(), Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture))).ToList();
}
