using NSubstitute;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.UpdateInvoiceItems;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using static Stockly.Application.Tests.Support.InvoiceTestContext;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;
using DomainInvoiceWarning = Stockly.Domain.Invoices.InvoiceWarning;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>I4 handler (docs/design/backend.md §9.1, §17.1 item 11). Request-shape rules: <see cref="UpdateInvoiceItemsValidatorTests"/>.</summary>
public sealed class UpdateInvoiceItemsCommandHandlerTests
{
    private static readonly Guid Panadol = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e1");
    private static readonly Guid Strip = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e2");
    private static readonly Guid Box = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e3");
    private static readonly Guid Other = Guid.Parse("0192f0a0-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherUnit = Guid.Parse("0192f0a0-0000-7000-8000-0000000000f2");

    private readonly InvoiceTestContext _context = new();
    private readonly InvoiceDetail _detail = new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Contracts.Invoices.InvoiceStatus.NeedsReview, null, null, null, null, null, null, [], null, [], Now.UtcDateTime, null, null);

    public UpdateInvoiceItemsCommandHandlerTests()
    {
        GivenProducts(new ProductUnitsInfo(Panadol, false, [Strip, Box]), new ProductUnitsInfo(Other, false, [OtherUnit]));
        _context.Queries.FindDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_detail);
    }

    [Fact]
    public async Task Handle_ValidLines_ReplacesLinesHeaderAndWarningsAndReturnsTheProjection()
    {
        var invoice = ReviewInvoice();
        var previous = invoice.Items.ToList();
        var supplierId = Guid.CreateVersion7();
        _context.Suppliers.FindIdByNormalizedNameAsync("شركه الادويه", Arg.Any<CancellationToken>()).Returns(supplierId);

        var result = await Handle(Command(
            invoice.Id,
            [Existing(2, Panadol, Strip, "4", "10.00"), New(1, "دواء جديد", "شريط", "15.00", "1.500", "20.00")],
            supplierName: " شركة الأدوية ",
            totalAmount: "100.00"));

        result.Value.Should().BeSameAs(_detail);
        invoice.Items.OrderBy(item => item.LineNo).Select(item => (item.LineNo, item.ProductId, item.NewProductName, item.LineTotal))
            .Should().Equal((1, (Guid?)null, "دواء جديد", 30.00m), (2, Panadol, null, 40.00m));
        invoice.SupplierId.Should().Be(supplierId);
        invoice.SupplierNameRaw.Should().Be("شركة الأدوية");
        invoice.TotalAmount.Should().Be(100.00m);
        invoice.ComputedTotal.Should().Be(70.00m);
        invoice.Warnings.Should().Equal([DomainInvoiceWarning.TotalMismatch], "only Review-stage rules run and replace the warnings");
        _context.Invoices.Received(1).RemoveItems(Arg.Is<IReadOnlyCollection<InvoiceItem>>(items => items.SequenceEqual(previous)));
        _context.UnitOfWork.SaveCount.Should().Be(1);
        invoice.Status.Should().Be(DomainInvoiceStatus.NeedsReview);
    }

    [Fact]
    public async Task Handle_SameProductAndUnitOnTheSameLine_KeepsMatchConfidenceOtherwiseClearsIt()
    {
        var invoice = ReviewInvoice();

        await Handle(Command(invoice.Id, [Existing(1, Panadol, Box, "1", "1.00"), Existing(2, Panadol, Strip, "1", "1.00")]));

        invoice.Items.OrderBy(item => item.LineNo).Select(item => item.MatchConfidence).Should().Equal(0.90m, null);
    }

    [Fact]
    public async Task Handle_UnitChangedOnAMatchedLine_ClearsMatchConfidence()
    {
        var invoice = ReviewInvoice();

        await Handle(Command(invoice.Id, [Existing(1, Panadol, Strip, "1", "1.00")]));

        invoice.Items.Single().MatchConfidence.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LineWithNeitherProductNorNewProduct_LineProductRequiredBeforeAnyLock()
    {
        var invoice = ReviewInvoice();

        var result = await Handle(Command(invoice.Id, [new InvoiceLineRequest(1, "x", "1", "1.00")]));

        result.Error.Should().Be(InvoiceErrors.LineProductRequired);
        await _context.Invoices.DidNotReceive().FindForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("1.0001", "1.00", "Precision.TooManyDecimals")]
    [InlineData("1", "1.005", "Precision.TooManyDecimals")]
    [InlineData("1", "-1.00", "Precision.Negative")]
    [InlineData("100000000000", "1.00", "Precision.OutOfRange")]
    public async Task Handle_DecimalOutOfPrecision_PrecisionErrorAndNothingSaved(string quantity, string unitPrice, string code)
    {
        var invoice = ReviewInvoice();

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, Strip, quantity, unitPrice)]));

        result.Error.Code.Should().Be(code);
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NewProductSellPriceNegative_PrecisionNegative()
    {
        var invoice = ReviewInvoice();

        var result = await Handle(Command(invoice.Id, [New(1, "جديد", "علبة", "-1.00", "1", "1.00")]));

        result.Error.Code.Should().Be(PrecisionErrors.Negative("x").Code);
    }

    [Fact]
    public async Task Handle_InvoiceMissingOrAnotherShops_NotFound()
    {
        // Tenant-filtered repository: another shop's invoice is null, like a missing one (rule 10).
        var result = await Handle(Command(Guid.CreateVersion7(), [Existing(1, Panadol, Strip, "1", "1.00")]));

        result.Error.Should().Be(InvoiceErrors.NotFound);
    }

    [Fact]
    public async Task Handle_InvoiceStillProcessing_NotEditable()
    {
        var invoice = _context.ProcessingInvoice();

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, Strip, "1", "1.00")]));

        result.Error.Should().Be(InvoiceErrors.NotEditable);
    }

    [Fact]
    public async Task Handle_InvoiceFailed_NotEditable()
    {
        var invoice = _context.ProcessingInvoice();
        invoice.MarkFailed(InvoiceFailureReason.AiNoItems, null, Now);

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, Strip, "1", "1.00")]));

        result.Error.Should().Be(InvoiceErrors.NotEditable);
    }

    [Fact]
    public async Task Handle_ProductMissingOrAnotherShops_ProductNotFound()
    {
        var invoice = ReviewInvoice();

        var result = await Handle(Command(invoice.Id, [Existing(1, Guid.CreateVersion7(), Strip, "1", "1.00")]));

        result.Error.Should().Be(ProductErrors.NotFound);
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_UnitOfAnotherProduct_UnitNotOfProduct()
    {
        var invoice = ReviewInvoice();
        _context.Products.UnitExistsAsync(OtherUnit, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, OtherUnit, "1", "1.00")]));

        result.Error.Should().Be(InvoiceErrors.UnitNotOfProduct);
    }

    [Fact]
    public async Task Handle_UnitDoesNotExist_ProductUnitNotFound()
    {
        var invoice = ReviewInvoice();

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, Guid.CreateVersion7(), "1", "1.00")]));

        result.Error.Should().Be(ProductUnitErrors.NotFound);
    }

    [Fact]
    public async Task Handle_ArchivedProductAlreadyOnThatLine_Kept()
    {
        var invoice = ReviewInvoice();
        GivenProducts(new ProductUnitsInfo(Panadol, true, [Strip, Box]));

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, Strip, "2", "1.00")]));

        result.IsSuccess.Should().BeTrue();
        invoice.Items.Single().Quantity.Should().Be(2m);
    }

    [Fact]
    public async Task Handle_ArchivedProductChosenForAnotherLine_ProductNotFound()
    {
        var invoice = ReviewInvoice();
        GivenProducts(new ProductUnitsInfo(Panadol, true, [Strip, Box]));

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, Box, "1", "1.00"), Existing(2, Panadol, Strip, "1", "1.00")]));

        result.Error.Should().Be(ProductErrors.NotFound, "an archived product is kept only where it already was (§17.1 item 11)");
    }

    [Fact]
    public async Task Handle_HundredLines_Saved()
    {
        var invoice = ReviewInvoice();
        var lines = Enumerable.Range(1, InvoiceLimits.MaxLines).Select(i => Existing(i, Panadol, Strip, "1", "1.00")).ToList();

        var result = await Handle(Command(invoice.Id, lines));

        result.IsSuccess.Should().BeTrue();
        invoice.Items.Should().HaveCount(InvoiceLimits.MaxLines);
    }

    [Fact]
    public async Task Handle_NoTenant_NotFound()
    {
        var invoice = ReviewInvoice();
        _context.CurrentTenant.ShopId.Returns((Guid?)null);
        _context.Invoices.FindForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Invoice?)null);

        var result = await Handle(Command(invoice.Id, [Existing(1, Panadol, Strip, "1", "1.00")]));

        result.Error.Should().Be(InvoiceErrors.NotFound);
    }

    internal static UpdateInvoiceItemsCommand Command(
        Guid invoiceId, IReadOnlyList<InvoiceLineRequest> lines, string? supplierName = null, string? totalAmount = null) =>
        new(invoiceId, lines, supplierName, "INV-1", new DateOnly(2026, 10, 10), totalAmount);

    internal static InvoiceLineRequest Existing(int lineNo, Guid productId, Guid unitId, string quantity, string unitPrice) =>
        new(lineNo, "بنادول", quantity, unitPrice, productId, unitId);

    internal static InvoiceLineRequest New(int lineNo, string name, string baseUnit, string sellPrice, string quantity, string unitPrice) =>
        new(lineNo, name, quantity, unitPrice, NewProduct: new InvoiceNewProductDto(name, baseUnit, sellPrice));

    // NeedsReview with line 1 matched to Panadol in boxes at 0.90.
    private Invoice ReviewInvoice()
    {
        var invoice = _context.ProcessingInvoice();
        invoice.CompleteExtraction(
            InvoiceHeader.Empty,
            [new InvoiceLineDraft("بنادول", 1m, 1m, null, null, new InvoiceLineMatch(Panadol, Box, 0.90m))],
            [],
            null,
            null,
            Now).IsSuccess.Should().BeTrue();
        return invoice;
    }

    private void GivenProducts(params ProductUnitsInfo[] products) =>
        _context.Products.FindUnitsInfoAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(products.ToList());

    private Task<Result<InvoiceDetail>> Handle(UpdateInvoiceItemsCommand command) =>
        new UpdateInvoiceItemsCommandHandler(
                _context.Invoices,
                _context.Queries,
                _context.Products,
                _context.Suppliers,
                _context.InvoiceValidator(),
                _context.UnitOfWork)
            .Handle(command, CancellationToken.None);
}
