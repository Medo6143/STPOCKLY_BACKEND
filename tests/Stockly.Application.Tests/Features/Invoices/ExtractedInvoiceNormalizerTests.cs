using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Application.Features.Invoices.ProcessInvoice;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>docs/design/backend.md §7.2: typed values, nothing rounded, unreadable lines counted and dropped.</summary>
public sealed class ExtractedInvoiceNormalizerTests
{
    [Fact]
    public void Normalize_ReadableInvoice_TypedHeaderAndLines()
    {
        var extraction = new ExtractedInvoice(
            "  شركة الأدوية  ",
            " INV-1043 ",
            "2026-10-10",
            "1250.50",
            false,
            [new ExtractedInvoiceLine(" بنادول اكسترا ", "علبة", "10", "120.00", "1200.00", "2027-03-31", " B12 ")]);

        var normalized = ExtractedInvoiceNormalizer.Normalize(extraction);

        normalized.Header.Should().Be(new InvoiceHeader("شركة الأدوية", "INV-1043", new DateOnly(2026, 10, 10), 1250.50m));
        normalized.Lines.Should().Equal(new InvoiceLineDraft("بنادول اكسترا", 10m, 120.00m, new DateOnly(2027, 3, 31), "B12"));
        normalized.UnreadableLineCount.Should().Be(0);
        normalized.Truncated.Should().BeFalse();
        normalized.ComputedTotal.Should().Be(1200.00m);
    }

    [Theory]
    [InlineData("10/10/2026")]
    [InlineData("2026-13-01")]
    [InlineData("tomorrow")]
    public void Normalize_InvalidDates_BecomeNull(string date)
    {
        var normalized = ExtractedInvoiceNormalizer.Normalize(Invoice(header: date, Line("x", "1", "1", expiry: date)));

        normalized.Header.InvoiceDate.Should().BeNull();
        normalized.Lines.Single().ExpiryDate.Should().BeNull();
    }

    [Theory]
    [InlineData("12.345")]
    [InlineData("-5")]
    [InlineData("1,250.00")]
    [InlineData("١٢٥")]
    [InlineData("1e3")]
    public void Normalize_TotalNotAValidAmount_BecomesNullNotRounded(string total)
    {
        ExtractedInvoiceNormalizer.Normalize(Invoice(total: total)).Header.TotalAmount.Should().BeNull();
    }

    public static TheoryData<string?, string?, string?, string?> UnreadableLines() => new()
    {
        { null, "1", "1.00", null },
        { "  ـ  ", "1", "1.00", null },
        { "بنادول", null, "1.00", null },
        { "بنادول", "0", "1.00", null },
        { "بنادول", "1.0005", "1.00", null },
        { "بنادول", "1", "1.005", null },
        { "بنادول", "1", null, null },
        { "بنادول", "3", null, "10.00" },
    };

    [Theory]
    [MemberData(nameof(UnreadableLines))]
    public void Normalize_UnreadableLine_CountedAndDropped(string? name, string? quantity, string? unitPrice, string? lineTotal)
    {
        var normalized = ExtractedInvoiceNormalizer.Normalize(Invoice(lines: [new ExtractedInvoiceLine(name, null, quantity, unitPrice, lineTotal, null, null)]));

        normalized.Lines.Should().BeEmpty();
        normalized.UnreadableLineCount.Should().Be(1);
    }

    [Fact]
    public void Normalize_MissingUnitPriceButExactLineTotal_DerivesThePrice()
    {
        var normalized = ExtractedInvoiceNormalizer.Normalize(Invoice(lines: [new ExtractedInvoiceLine("بنادول", null, "4", null, "50.00", null, null)]));

        normalized.Lines.Single().UnitPrice.Should().Be(12.50m);
    }

    [Fact]
    public void Normalize_MixedLines_KeepsOrderOfReadableLines()
    {
        var normalized = ExtractedInvoiceNormalizer.Normalize(Invoice(lines:
        [
            Line("أول", "1", "1.00"),
            new ExtractedInvoiceLine(null, null, null, null, null, null, null),
            Line("ثالث", "2", "3.00"),
        ]));

        normalized.Lines.Select(line => line.RawName).Should().Equal("أول", "ثالث");
        normalized.UnreadableLineCount.Should().Be(1);
    }

    [Fact]
    public void Normalize_ReadableLines_MatchInputsNumberedLikeTheLinesWithNormalizedNameAndUnit()
    {
        var normalized = ExtractedInvoiceNormalizer.Normalize(Invoice(lines:
        [
            Line("بنادول إكسترا", "1", "1.00") with { Unit = " عُلبة " },
            new ExtractedInvoiceLine(null, null, null, null, null, null, null),
            Line("Panadol  Extra", "2", "3.00") with { Unit = "  " },
        ]));

        normalized.MatchInputs.Should().Equal(
            new MatchInput(1, "بنادول اكسترا", "علبه"),
            new MatchInput(2, "panadol extra", null));
    }

    private static ExtractedInvoice Invoice(string? header = null, params ExtractedInvoiceLine[] lines) =>
        new(null, null, header, null, false, lines.Length == 0 ? [Line("x", "1", "1")] : lines);

    private static ExtractedInvoice Invoice(string total) =>
        new(null, null, null, total, false, [Line("x", "1", "1")]);

    private static ExtractedInvoiceLine Line(string name, string quantity, string unitPrice, string? expiry = null) =>
        new(name, null, quantity, unitPrice, null, expiry, null);
}
