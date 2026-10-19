using Stockly.Domain.Payments;

namespace Stockly.Domain.Tests.Payments;

/// <summary>EGP ↔ piastres with decimal arithmetic only (AGENTS.md rule 6, ADR-0015 §6).</summary>
public sealed class PiastresTests
{
    [Theory]
    [InlineData("199.50", 19950)]
    [InlineData("0.01", 1)]
    [InlineData("0.10", 10)]
    [InlineData("1", 100)]
    [InlineData("1234567.89", 123456789)]
    [InlineData("0.29", 29)]
    public void FromPounds_TwoDecimalAmount_IsExactInteger(string amount, long piastres)
    {
        Piastres.FromPounds(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(piastres);
    }

    [Theory]
    [InlineData("0.005")]
    [InlineData("-1.00")]
    public void FromPounds_InvalidAmount_Throws(string amount)
    {
        var act = () => Piastres.FromPounds(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(19950, "199.50")]
    [InlineData(1, "0.01")]
    [InlineData(100, "1.00")]
    public void ToPounds_Piastres_IsExactDecimal(long piastres, string amount)
    {
        Piastres.ToPounds(piastres).Should().Be(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RoundTrip_EveryCentUpToTenPounds_IsLossless()
    {
        for (var cents = 0L; cents <= 1000; cents++)
        {
            Piastres.FromPounds(Piastres.ToPounds(cents)).Should().Be(cents);
        }
    }
}
