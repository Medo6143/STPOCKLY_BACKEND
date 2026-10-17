using Stockly.Domain.Invoices;

namespace Stockly.Domain.Tests.Invoices;

/// <summary>AGENTS.md rule 16: mismatch when |computed − total| &gt; 2% of total.</summary>
public sealed class TotalsTolerancePolicyTests
{
    [Theory]
    [InlineData("100.00", "100.00", false)]
    [InlineData("100.00", "102.00", false)]
    [InlineData("100.00", "98.00", false)]
    [InlineData("100.00", "102.01", true)]
    [InlineData("100.00", "97.99", true)]
    [InlineData("1250.00", "1275.00", false)]
    [InlineData("1250.00", "1275.01", true)]
    [InlineData("0.00", "0.00", false)]
    [InlineData("0.00", "0.01", true)]
    public void IsMismatch_PrintedTotal_UsesTwoPercentOfTotal(string total, string computed, bool expected)
    {
        TotalsTolerancePolicy.IsMismatch(decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(computed, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(expected);
    }

    [Fact]
    public void IsMismatch_NoPrintedTotal_NeverMismatch()
    {
        TotalsTolerancePolicy.IsMismatch(null, 999m).Should().BeFalse();
    }
}
