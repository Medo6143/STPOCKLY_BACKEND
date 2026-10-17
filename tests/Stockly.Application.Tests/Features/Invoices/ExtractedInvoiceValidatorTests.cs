using System.Text.Json;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Features.Invoices.ProcessInvoice;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>AI output is untrusted (AGENTS.md rule 15): strict parse (<see cref="AiJson"/>) and structural limits.</summary>
public sealed class ExtractedInvoiceValidatorTests
{
    private const string ValidJson = """
        {"supplierName":"شركة","invoiceNumber":"1","invoiceDate":null,"totalAmount":"10.00","truncated":false,
         "items":[{"name":"بنادول","unit":null,"quantity":"1","unitPrice":"10.00","lineTotal":"10.00","expiryDate":null,"batchNo":null}]}
        """;

    private readonly ExtractedInvoiceValidator _validator = new();

    [Fact]
    public void Parse_SchemaShapedJson_ReadsEveryField()
    {
        var extraction = AiJson.Parse(ValidJson);

        extraction.SupplierName.Should().Be("شركة");
        extraction.Items.Should().ContainSingle().Which.UnitPrice.Should().Be("10.00");
        _validator.Validate(extraction).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"supplierName":null,"invoiceNumber":null,"invoiceDate":null,"totalAmount":null,"truncated":false,"items":[],"extra":1}""")]
    [InlineData("""{"supplierName":null,"invoiceNumber":null,"invoiceDate":null,"truncated":false,"items":[]}""")]
    [InlineData("""{"supplierName":null,"invoiceNumber":null,"invoiceDate":null,"totalAmount":10.5,"truncated":false,"items":[]}""")]
    [InlineData("""{"supplierName":null,"invoiceNumber":null,"invoiceDate":null,"totalAmount":null,"truncated":false,"items":null}""")]
    [InlineData("""{"supplierName":null,"invoiceNumber":null,"invoiceDate":null,"totalAmount":null,"truncated":false,"items":[],}""")]
    [InlineData("""{"supplierName":null /* c */,"invoiceNumber":null,"invoiceDate":null,"totalAmount":null,"truncated":false,"items":[]}""")]
    [InlineData("```json\n{}\n```")]
    [InlineData("null")]
    public void Parse_OutsideTheSchema_Throws(string json)
    {
        var act = () => AiJson.Parse(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Validate_MoreThanHundredItems_Invalid()
    {
        var lines = Enumerable.Range(0, 101).Select(_ => new ExtractedInvoiceLine("x", null, "1", "1", null, null, null)).ToList();

        _validator.Validate(new ExtractedInvoice(null, null, null, null, true, lines)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NullLine_Invalid()
    {
        _validator.Validate(new ExtractedInvoice(null, null, null, null, false, [null!])).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(201, 1, 1)]
    [InlineData(1, 65, 1)]
    [InlineData(1, 1, 301)]
    public void Validate_TextOverItsLimit_Invalid(int supplierLength, int numberLength, int nameLength)
    {
        var extraction = new ExtractedInvoice(
            new string('s', supplierLength),
            new string('1', numberLength),
            null,
            null,
            false,
            [new ExtractedInvoiceLine(new string('n', nameLength), null, "1", "1", null, null, null)]);

        _validator.Validate(extraction).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyItems_ValidStructure()
    {
        _validator.Validate(new ExtractedInvoice(null, null, null, null, false, [])).IsValid.Should().BeTrue("an empty result is valid; the job decides it is AiNoItems");
    }
}
