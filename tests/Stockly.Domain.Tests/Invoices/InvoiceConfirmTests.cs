using Stockly.Domain.Invoices;

namespace Stockly.Domain.Tests.Invoices;

/// <summary><see cref="Invoice.Confirm"/> (contract I5, docs/design/backend.md §10.4, §11 step 9).</summary>
public sealed class InvoiceConfirmTests
{
    private static readonly DateTimeOffset Reviewed = TestData.Now.AddMinutes(1);
    private static readonly DateTimeOffset Confirmed = TestData.Now.AddMinutes(5);

    [Fact]
    public void Confirm_ResolvedLines_SetsStatusSupplierUserAndEveryLine()
    {
        var invoice = Reviewed2Lines(out var newLineId);
        var supplierId = Guid.CreateVersion7();
        var createdProduct = Guid.CreateVersion7();

        var result = invoice.Confirm(TestData.UserId, supplierId, new Dictionary<Guid, Guid> { [newLineId] = createdProduct }, Confirmed);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Confirmed);
        invoice.ConfirmedAt.Should().Be(Confirmed);
        invoice.ConfirmedByUserId.Should().Be(TestData.UserId);
        invoice.SupplierId.Should().Be(supplierId);
        invoice.Items.Should().OnlyContain(item => item.ConfirmedAt == Confirmed);
        invoice.Items.Single(item => item.Id == newLineId).CreatedProductId.Should().Be(createdProduct);
        invoice.Items.Single(item => item.Id != newLineId).CreatedProductId.Should().BeNull();
        invoice.Items.Single(item => item.Id == newLineId).ProductId.Should().BeNull("never both a product and a new product");
    }

    [Fact]
    public void Confirm_NotNeedsReview_NotConfirmableAndNothingChanges()
    {
        var processing = Invoice.CreateForScan(TestData.ShopId, Guid.CreateVersion7(), TestData.UserId, new string('a', 64), TestData.Now, id => $"k/{id}").Value;
        var confirmedTwice = Reviewed2Lines(out var lineId);
        confirmedTwice.Confirm(TestData.UserId, null, new Dictionary<Guid, Guid> { [lineId] = Guid.CreateVersion7() }, Confirmed);

        processing.Confirm(TestData.UserId, null, new Dictionary<Guid, Guid>(), Confirmed).Error.Should().Be(InvoiceErrors.NotConfirmable);
        confirmedTwice.Confirm(TestData.UserId, null, new Dictionary<Guid, Guid> { [lineId] = Guid.CreateVersion7() }, Confirmed.AddDays(1))
            .Error.Should().Be(InvoiceErrors.NotConfirmable);
        processing.Status.Should().Be(InvoiceStatus.Processing);
        confirmedTwice.ConfirmedAt.Should().Be(Confirmed);
    }

    [Fact]
    public void Confirm_UnmatchedLine_LineProductRequired()
    {
        var invoice = NewInvoice();
        invoice.CompleteExtraction(InvoiceHeader.Empty, [new InvoiceLineDraft("x", 1m, 1m, null, null)], [], null, null, Reviewed);

        invoice.HasUnresolvedLine.Should().BeTrue();
        invoice.Confirm(TestData.UserId, null, new Dictionary<Guid, Guid>(), Confirmed).Error.Should().Be(InvoiceErrors.LineProductRequired);
        invoice.Items.Single().ConfirmedAt.Should().BeNull();
    }

    [Fact]
    public void Confirm_CreatedProductsDoNotMatchTheNewProductLines_Refused()
    {
        var invoice = Reviewed2Lines(out var newLineId);
        var existingLineId = invoice.Items.Single(item => item.Id != newLineId).Id;

        invoice.Confirm(TestData.UserId, null, new Dictionary<Guid, Guid>(), Confirmed).Error.Should().Be(InvoiceErrors.CreatedProductsMismatch);
        invoice.Confirm(TestData.UserId, null, new Dictionary<Guid, Guid> { [newLineId] = Guid.Empty }, Confirmed)
            .Error.Should().Be(InvoiceErrors.CreatedProductsMismatch);
        invoice.Confirm(
                TestData.UserId,
                null,
                new Dictionary<Guid, Guid> { [newLineId] = Guid.CreateVersion7(), [existingLineId] = Guid.CreateVersion7() },
                Confirmed)
            .Error.Should().Be(InvoiceErrors.CreatedProductsMismatch);
        invoice.Status.Should().Be(InvoiceStatus.NeedsReview);
    }

    [Fact]
    public void Confirm_NoUser_UserRequired()
    {
        var invoice = Reviewed2Lines(out var newLineId);

        invoice.Confirm(Guid.Empty, null, new Dictionary<Guid, Guid> { [newLineId] = Guid.CreateVersion7() }, Confirmed)
            .Error.Should().Be(InvoiceErrors.UserRequired);
    }

    private static Invoice NewInvoice() =>
        Invoice.CreateForScan(TestData.ShopId, Guid.CreateVersion7(), TestData.UserId, new string('a', 64), TestData.Now, id => $"k/{id}").Value;

    // NeedsReview after I4: line 1 an existing product, line 2 a new product.
    private static Invoice Reviewed2Lines(out Guid newLineId)
    {
        var invoice = NewInvoice();
        invoice.CompleteExtraction(InvoiceHeader.Empty, [new InvoiceLineDraft("x", 1m, 1m, null, null)], [], null, null, Reviewed);
        invoice.ReplaceLines(
            InvoiceHeader.Empty,
            [
                new InvoiceReviewLine(1, "بنادول", 2m, 3m, null, null, new InvoiceLineProduct(Guid.CreateVersion7(), Guid.CreateVersion7()), null),
                new InvoiceReviewLine(2, "جديد", 1m, 2m, null, null, null, new InvoiceNewProduct("جديد", "قطعة", 4m)),
            ],
            null,
            []).IsSuccess.Should().BeTrue();
        newLineId = invoice.Items.Single(item => item.NewProductName is not null).Id;
        return invoice;
    }
}
