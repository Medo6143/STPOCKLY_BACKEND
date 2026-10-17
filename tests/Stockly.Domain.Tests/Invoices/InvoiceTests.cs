using Stockly.Domain.Common;
using Stockly.Domain.Invoices;

namespace Stockly.Domain.Tests.Invoices;

public sealed class InvoiceTests
{
    private static readonly Guid ClientRequestId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000c1");
    private static readonly string Sha256 = new('a', InvoiceLimits.ImageSha256Length);
    private static readonly DateTimeOffset Later = TestData.Now.AddMinutes(1);

    [Fact]
    public void CreateForScan_ValidInput_IsProcessingWithKeyBuiltFromItsId()
    {
        var invoice = NewInvoice();

        invoice.Status.Should().Be(InvoiceStatus.Processing);
        invoice.ShopId.Should().Be(TestData.ShopId);
        invoice.ClientRequestId.Should().Be(ClientRequestId);
        invoice.CreatedByUserId.Should().Be(TestData.UserId);
        invoice.ImageSha256.Should().Be(Sha256);
        invoice.ImageObjectKey.Should().Be($"key/{invoice.Id}");
        invoice.CreatedAt.Should().Be(TestData.Now);
        invoice.Items.Should().BeEmpty();
        invoice.Warnings.Should().BeEmpty();
        invoice.ProcessedAt.Should().BeNull();
    }

    public static TheoryData<Guid, Guid, Guid, string, string> InvalidScans() => new()
    {
        { Guid.Empty, ClientRequestId, TestData.UserId, Sha256, nameof(InvoiceErrors.ShopRequired) },
        { TestData.ShopId, Guid.Empty, TestData.UserId, Sha256, nameof(InvoiceErrors.ClientRequestIdRequired) },
        { TestData.ShopId, ClientRequestId, Guid.Empty, Sha256, nameof(InvoiceErrors.UserRequired) },
        { TestData.ShopId, ClientRequestId, TestData.UserId, "abc", nameof(InvoiceErrors.ImageHashInvalid) },
        { TestData.ShopId, ClientRequestId, TestData.UserId, new string('A', 64), nameof(InvoiceErrors.ImageHashInvalid) },
    };

    [Theory]
    [MemberData(nameof(InvalidScans))]
    public void CreateForScan_InvalidInput_Fails(Guid shopId, Guid clientRequestId, Guid userId, string sha256, string errorName)
    {
        var result = Invoice.CreateForScan(shopId, clientRequestId, userId, sha256, TestData.Now, id => $"key/{id}");

        result.Error.Should().Be(typeof(InvoiceErrors).GetField(errorName)!.GetValue(null));
    }

    [Fact]
    public void CreateForScan_BlankKey_Fails()
    {
        var result = Invoice.CreateForScan(TestData.ShopId, ClientRequestId, TestData.UserId, Sha256, TestData.Now, _ => " ");

        result.Error.Should().Be(InvoiceErrors.ImageKeyRequired);
    }

    [Fact]
    public void CompleteExtraction_ValidLines_NeedsReviewWithNumberedLinesAndTotals()
    {
        var invoice = NewInvoice();
        var header = new InvoiceHeader("شركة الأدوية", "INV-1", new DateOnly(2026, 10, 10), 250.00m);

        var result = invoice.CompleteExtraction(
            header,
            [Line("بنادول", 10m, 12.50m), Line("كونجستال", 1.5m, 83.33m)],
            [InvoiceWarning.TotalMismatch, InvoiceWarning.TotalMismatch],
            "{\"items\":[]}",
            supplierId: null,
            Later);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.NeedsReview);
        invoice.SupplierNameRaw.Should().Be("شركة الأدوية");
        invoice.InvoiceNumber.Should().Be("INV-1");
        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 10, 10));
        invoice.TotalAmount.Should().Be(250.00m);
        invoice.Items.Select(item => item.LineNo).Should().Equal(1, 2);
        invoice.Items.Select(item => item.LineTotal).Should().Equal(125.00m, 125.00m);
        invoice.ComputedTotal.Should().Be(250.00m);
        invoice.Warnings.Should().Equal(InvoiceWarning.TotalMismatch);
        invoice.RawAiJson.Should().Be("{\"items\":[]}");
        invoice.ProcessedAt.Should().Be(Later);
        invoice.Items.Should().OnlyContain(item => item.InvoiceId == invoice.Id && item.ShopId == TestData.ShopId && item.ProductId == null);
    }

    [Fact]
    public void CompleteExtraction_NoLines_FailsAndStaysProcessing()
    {
        var invoice = NewInvoice();

        var result = invoice.CompleteExtraction(InvoiceHeader.Empty, [], [], null, null, Later);

        result.Error.Should().Be(InvoiceErrors.LinesRequired);
        invoice.Status.Should().Be(InvoiceStatus.Processing);
    }

    [Fact]
    public void CompleteExtraction_MoreThanMaxLines_Fails()
    {
        var invoice = NewInvoice();
        var lines = Enumerable.Range(1, InvoiceLimits.MaxLines + 1).Select(i => Line($"item {i}", 1m, 1m)).ToList();

        var result = invoice.CompleteExtraction(InvoiceHeader.Empty, lines, [], null, null, Later);

        result.Error.Should().Be(InvoiceErrors.TooManyLines);
        invoice.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(" ", 1, nameof(InvoiceErrors.LineNameRequired))]
    [InlineData("بنادول", 0, nameof(InvoiceErrors.LineQuantityNotPositive))]
    public void CompleteExtraction_InvalidLine_FailsAndChangesNothing(string name, int quantity, string errorName)
    {
        var invoice = NewInvoice();

        var result = invoice.CompleteExtraction(InvoiceHeader.Empty, [Line("ok", 1m, 1m), Line(name, quantity, 1m)], [], null, null, Later);

        result.Error.Should().Be(typeof(InvoiceErrors).GetField(errorName)!.GetValue(null));
        invoice.Status.Should().Be(InvoiceStatus.Processing);
        invoice.Items.Should().BeEmpty();
    }

    [Fact]
    public void CompleteExtraction_QuantityWithFourDecimals_RejectedNotRounded()
    {
        var invoice = NewInvoice();

        var result = invoice.CompleteExtraction(InvoiceHeader.Empty, [Line("بنادول", 1.0005m, 1m)], [], null, null, Later);

        result.Error.Code.Should().Be(PrecisionErrors.TooManyDecimals("x", 3).Code);
    }

    [Fact]
    public void MarkFailed_Processing_IsFailedWithReasonAndRawJson()
    {
        var invoice = NewInvoice();

        var result = invoice.MarkFailed(InvoiceFailureReason.AiOutputInvalid, "\"not json\"", Later);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Failed);
        invoice.FailureReason.Should().Be(InvoiceFailureReason.AiOutputInvalid);
        invoice.RawAiJson.Should().Be("\"not json\"");
        invoice.ProcessedAt.Should().Be(Later);
    }

    [Fact]
    public void MarkFailed_AlreadyFailed_IsTerminal()
    {
        var invoice = NewInvoice();
        invoice.MarkFailed(InvoiceFailureReason.ImageMissing, null, Later);

        invoice.MarkFailed(InvoiceFailureReason.Unexpected, null, Later).Error.Should().Be(InvoiceErrors.NotProcessing);
        invoice.CompleteExtraction(InvoiceHeader.Empty, [Line("x", 1m, 1m)], [], null, null, Later).Error.Should().Be(InvoiceErrors.NotProcessing);
        invoice.FailureReason.Should().Be(InvoiceFailureReason.ImageMissing);
    }

    [Fact]
    public void CompleteExtraction_AlreadyNeedsReview_SecondRunChangesNothing()
    {
        var invoice = NewInvoice();
        invoice.CompleteExtraction(InvoiceHeader.Empty, [Line("x", 1m, 1m)], [], null, null, Later);

        var second = invoice.CompleteExtraction(InvoiceHeader.Empty, [Line("y", 2m, 2m)], [], null, null, Later);

        second.Error.Should().Be(InvoiceErrors.NotProcessing);
        invoice.Items.Should().ContainSingle().Which.RawName.Should().Be("x");
        invoice.MarkFailed(InvoiceFailureReason.Unexpected, null, Later).Error.Should().Be(InvoiceErrors.NotProcessing);
        invoice.Status.Should().Be(InvoiceStatus.NeedsReview);
    }

    [Fact]
    public void Enums_StoredValues_NeverChange()
    {
        ((int)InvoiceStatus.Processing).Should().Be(1);
        ((int)InvoiceStatus.NeedsReview).Should().Be(2);
        ((int)InvoiceStatus.Confirmed).Should().Be(3);
        ((int)InvoiceStatus.Failed).Should().Be(4);
        Enum.GetNames<InvoiceFailureReason>().Should().Equal("ImageMissing", "AiUnavailable", "AiOutputInvalid", "AiNoItems", "Unexpected");
        Enum.GetNames<InvoiceWarning>().Should().Equal("TotalMismatch", "ItemCapExceeded", "UnreadableLines", "DuplicateInvoiceNumber");
    }

    private static Invoice NewInvoice() =>
        Invoice.CreateForScan(TestData.ShopId, ClientRequestId, TestData.UserId, Sha256, TestData.Now, id => $"key/{id}").Value;

    private static InvoiceLineDraft Line(string name, decimal quantity, decimal unitPrice) =>
        new(name, quantity, unitPrice, ExpiryDate: null, BatchNo: null);
}
