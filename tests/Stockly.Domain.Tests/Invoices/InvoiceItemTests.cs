using System.Globalization;
using Stockly.Domain.Invoices;

namespace Stockly.Domain.Tests.Invoices;

/// <summary>line_total = round(quantity × unit price, 2, away from zero) (docs/design/backend.md §17.1 item 6).</summary>
public sealed class InvoiceItemTests
{
    [Theory]
    [InlineData("10", "12.50", "125.00")]
    [InlineData("1.333", "1.50", "2.00")]
    [InlineData("0.5", "0.01", "0.01")]
    [InlineData("0.125", "0.10", "0.01")]
    [InlineData("2.345", "1.00", "2.35")]
    [InlineData("1.5", "83.33", "125.00")]
    [InlineData("0.001", "0.01", "0.00")]
    public void ComputeLineTotal_RoundsToTwoDecimalsAwayFromZero(string quantity, string unitPrice, string expected)
    {
        var total = InvoiceItem.ComputeLineTotal(Parse(quantity), Parse(unitPrice));

        total.Should().Be(Parse(expected));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
