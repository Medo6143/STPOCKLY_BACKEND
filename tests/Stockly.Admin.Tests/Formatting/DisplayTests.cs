using Stockly.Admin.Formatting;

namespace Stockly.Admin.Tests.Formatting;

public sealed class DisplayTests
{
    [Theory]
    [InlineData("0.00012345", "$0.00012345")]
    [InlineData("1.50000000", "$1.50000000")]
    [InlineData("12345678.12345678", "$12345678.12345678")]
    [InlineData("0.00000000", "$0.00000000")]
    public void Usd_CostString_KeepsEveryDecimal(string cost, string expected) =>
        Display.Usd(cost).Should().Be(expected);

    [Fact]
    public void Usd_SumOfManyCosts_IsExact()
    {
        var sum = Enumerable.Repeat("0.10000001", 3).Sum(Display.ParseDecimal);

        Display.Usd(sum).Should().Be("$0.30000003");
    }

    [Theory]
    [InlineData("199.50", "199.50 EGP")]
    [InlineData("1250.00", "1,250.00 EGP")]
    public void Egp_MoneyString_ShowsTwoDecimals(string money, string expected) =>
        Display.Egp(money).Should().Be(expected);

    [Fact]
    public void ParseDecimal_IgnoresTheCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Display.ParseDecimal("1.25").Should().Be(1.25m);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
