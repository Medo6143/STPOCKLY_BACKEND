using NSubstitute;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Storage;
using Stockly.Application.Features.Invoices.ProcessInvoice;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Domain.Text;
using static Stockly.Application.Tests.Support.InvoiceTestContext;

namespace Stockly.Application.Tests.Features.Invoices;

public sealed class ProcessInvoiceCommandHandlerTests
{
    private const string Primary = "google/gemini-2.5-flash";
    private const string Fallback = "openai/gpt-4.1-mini";

    private static readonly StoredFile Image = new(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg");

    private readonly InvoiceTestContext _context = new();

    public ProcessInvoiceCommandHandlerTests()
    {
        _context.Storage.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Image);
    }

    [Fact]
    public async Task Handle_AiSucceeds_NeedsReviewWithLinesWarningsUsageRowsAndCache()
    {
        var invoice = _context.ProcessingInvoice();
        var extraction = Extraction(total: "300.00", Line("بنادول", "10", "12.50"), Line("كونجستال", "2", "50.00"));
        _context.Extractor.ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>()).Returns(new AiExtractionResult(
            extraction,
            null,
            Primary,
            AiJson.Serialize(extraction),
            [Attempt(Primary, AiCallOutcome.RateLimited, 429), Attempt(Primary, AiCallOutcome.Succeeded, 200, tokens: 900, cost: 0.0012m)]));

        var result = await Handle(invoice.Id);

        result.Value.Should().BeEquivalentTo(new ProcessInvoiceOutcome(
            InvoiceStatus.NeedsReview, null, [InvoiceWarning.TotalMismatch], 2, 2));
        invoice.Status.Should().Be(InvoiceStatus.NeedsReview);
        invoice.Items.Select(item => (item.LineNo, item.RawName, item.LineTotal))
            .Should().Equal((1, "بنادول", 125.00m), (2, "كونجستال", 100.00m));
        invoice.ComputedTotal.Should().Be(225.00m);
        invoice.RawAiJson.Should().Be(AiJson.Serialize(extraction));
        _context.AddedUsage.Should().HaveCount(2, "one AiUsageRecord per HTTP attempt, retries included (rule 18)");
        _context.AddedUsage.Should().OnlyContain(record => record.InvoiceId == invoice.Id && !record.CacheHit && record.Model == Primary);
        _context.AddedUsage[1].PromptTokens.Should().Be(900);
        _context.AddedUsage[1].CostUsd.Should().Be(0.0012m);
        await _context.AiCache.Received(1).SetAsync(ShopId, Sha256, Primary, extraction, Arg.Any<CancellationToken>());
        await _context.StatusCache.Received(1).SetAsync(
            ShopId, Arg.Is<InvoiceStatusSnapshot>(snapshot => snapshot.Status == InvoiceStatus.NeedsReview), Arg.Any<CancellationToken>());
        _context.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SamePhotoExtractedBefore_ReusesItWithoutCallingTheAi()
    {
        var invoice = _context.ProcessingInvoice();
        var earlier = Extraction(total: null, Line("بنادول", "1", "10.00"));
        _context.Invoices.FindReusableExtractionAsync(Sha256, invoice.Id, Arg.Any<CancellationToken>())
            .Returns(new ReusableExtraction(AiJson.Serialize(earlier), Fallback));

        var result = await Handle(invoice.Id);

        result.Value.Status.Should().Be(InvoiceStatus.NeedsReview);
        await _context.Extractor.DidNotReceive().ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>());
        await _context.AiCache.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _context.AddedUsage.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            CacheHit = true,
            Model = Fallback,
            PromptTokens = 0,
            CompletionTokens = 0,
            CostUsd = 0m,
            Outcome = AiCallOutcome.Succeeded,
        });
    }

    [Fact]
    public async Task Handle_EarlierOutputNoLongerValid_IsNotReused()
    {
        var invoice = _context.ProcessingInvoice();
        _context.Invoices.FindReusableExtractionAsync(Sha256, invoice.Id, Arg.Any<CancellationToken>())
            .Returns(new ReusableExtraction("{\"unexpected\": true}", Primary));
        GivenAiReturns(Extraction(total: null, Line("بنادول", "1", "10.00")));

        await Handle(invoice.Id);

        await _context.Extractor.Received(1).ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AiResultCacheHit_UsesItWithOneCacheHitRow()
    {
        var invoice = _context.ProcessingInvoice();
        var cached = Extraction(total: null, Line("بنادول", "1", "10.00"));
        _context.AiCache.GetAsync(ShopId, Sha256, Arg.Any<CancellationToken>())
            .Returns(new CachedExtraction("invoice-extraction-v1", Primary, cached));

        var result = await Handle(invoice.Id);

        result.Value.Should().BeEquivalentTo(new ProcessInvoiceOutcome(InvoiceStatus.NeedsReview, null, [], 1, 1));
        await _context.Extractor.DidNotReceive().ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>());
        _context.AddedUsage.Should().ContainSingle().Which.CacheHit.Should().BeTrue();
        invoice.RawAiJson.Should().Be(AiJson.Serialize(cached));
    }

    [Theory]
    [InlineData(InvoiceFailureReason.AiUnavailable)]
    [InlineData(InvoiceFailureReason.AiOutputInvalid)]
    [InlineData(InvoiceFailureReason.AiNoItems)]
    public async Task Handle_AiFails_FailedWithReasonAndEveryAttemptRecorded(InvoiceFailureReason reason)
    {
        var invoice = _context.ProcessingInvoice();
        _context.Extractor.ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>()).Returns(new AiExtractionResult(
            null,
            reason,
            null,
            "not json at all",
            [Attempt(Primary, AiCallOutcome.ServerError, 500), Attempt(Primary, AiCallOutcome.Timeout, null), Attempt(Fallback, AiCallOutcome.InvalidOutput, 200)]));

        var result = await Handle(invoice.Id);

        result.Value.Should().BeEquivalentTo(new ProcessInvoiceOutcome(InvoiceStatus.Failed, reason, [], 0, 3));
        invoice.Status.Should().Be(InvoiceStatus.Failed);
        invoice.FailureReason.Should().Be(reason);
        invoice.RawAiJson.Should().Be("\"not json at all\"", "content that is not JSON is stored as a JSON string literal");
        _context.AddedUsage.Select(record => record.Outcome)
            .Should().Equal(AiCallOutcome.ServerError, AiCallOutcome.Timeout, AiCallOutcome.InvalidOutput);
        await _context.AiCache.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ExtractedInvoice>(), Arg.Any<CancellationToken>());
        await _context.StatusCache.Received(1).SetAsync(
            ShopId, Arg.Is<InvoiceStatusSnapshot>(snapshot => snapshot.Status == InvoiceStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EveryLineUnreadable_FailedWithAiNoItems()
    {
        var invoice = _context.ProcessingInvoice();
        GivenAiReturns(Extraction(total: null, Line(null, "1", "1.00"), Line("بنادول", "0", "1.00"), Line("كونجستال", "1.0005", "1.00")));

        var result = await Handle(invoice.Id);

        result.Value.FailureReason.Should().Be(InvoiceFailureReason.AiNoItems);
        _context.AddedUsage.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_SomeLinesUnreadableAndTruncated_WarnsAndKeepsTheReadableOnes()
    {
        var invoice = _context.ProcessingInvoice();
        GivenAiReturns(Extraction(total: null, Line("بنادول", "1", "1.00"), Line(null, null, null)) with { Truncated = true });

        var result = await Handle(invoice.Id);

        result.Value.Warnings.Should().Equal(InvoiceWarning.ItemCapExceeded, InvoiceWarning.UnreadableLines);
        invoice.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_KnownSupplierAndSameNumberSeenBefore_LinksSupplierAndWarnsDuplicate()
    {
        var invoice = _context.ProcessingInvoice();
        var supplierId = Guid.CreateVersion7();
        _context.Suppliers.FindIdByNormalizedNameAsync(ArabicTextNormalizer.Normalize("شركة الأدوية"), Arg.Any<CancellationToken>())
            .Returns(supplierId);
        _context.Invoices.ExistsOtherWithNumberAsync(invoice.Id, supplierId, "INV-9", Arg.Any<CancellationToken>()).Returns(true);
        GivenAiReturns(Extraction(total: null, Line("بنادول", "1", "1.00")) with { SupplierName = " شركة الأدوية ", InvoiceNumber = "INV-9" });

        var result = await Handle(invoice.Id);

        result.Value.Warnings.Should().Equal(InvoiceWarning.DuplicateInvoiceNumber);
        invoice.SupplierId.Should().Be(supplierId);
        invoice.SupplierNameRaw.Should().Be("شركة الأدوية");
    }

    [Fact]
    public async Task Handle_ImageMissing_FailedWithoutAnyAiCallOrUsageRow()
    {
        var invoice = _context.ProcessingInvoice();
        _context.Storage.GetAsync(invoice.ImageObjectKey, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var result = await Handle(invoice.Id);

        result.Value.FailureReason.Should().Be(InvoiceFailureReason.ImageMissing);
        await _context.Extractor.DidNotReceive().ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>());
        _context.AddedUsage.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvoiceNoLongerProcessing_WritesNothing()
    {
        var invoice = _context.ProcessingInvoice();
        invoice.MarkFailed(InvoiceFailureReason.Unexpected, null, Now);

        var result = await Handle(invoice.Id);

        result.Error.Should().Be(InvoiceErrors.NotProcessing);
        await _context.Storage.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AnotherRunFinishedDuringTheAiCall_KeepsItsResultButRecordsTheCalls()
    {
        var invoice = _context.ProcessingInvoice();
        _context.Extractor.ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            invoice.MarkFailed(InvoiceFailureReason.Unexpected, null, Now);
            var extraction = Extraction(total: null, Line("بنادول", "1", "1.00"));
            return new AiExtractionResult(extraction, null, Primary, "{}", [Attempt(Primary, AiCallOutcome.Succeeded, 200)]);
        });

        var result = await Handle(invoice.Id);

        result.Error.Should().Be(InvoiceErrors.NotProcessing);
        invoice.Status.Should().Be(InvoiceStatus.Failed);
        _context.AddedUsage.Should().ContainSingle("the call happened and cost money (rule 18)");
        await _context.StatusCache.DidNotReceive().SetAsync(Arg.Any<Guid>(), Arg.Any<InvoiceStatusSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OtherShopsInvoice_NotFound()
    {
        // Tenant-filtered: another shop's invoice is invisible.
        var result = await Handle(Guid.CreateVersion7());

        result.Error.Should().Be(InvoiceErrors.NotFound);
        await _context.Storage.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoTenant_NotFound()
    {
        var invoice = _context.ProcessingInvoice();
        _context.CurrentTenant.ShopId.Returns((Guid?)null);

        var result = await Handle(invoice.Id);

        result.Error.Should().Be(InvoiceErrors.NotFound);
    }

    private void GivenAiReturns(ExtractedInvoice extraction) =>
        _context.Extractor.ExtractAsync(Arg.Any<AiExtractionRequest>(), Arg.Any<CancellationToken>()).Returns(new AiExtractionResult(
            extraction, null, Primary, AiJson.Serialize(extraction), [Attempt(Primary, AiCallOutcome.Succeeded, 200)]));

    private static ExtractedInvoice Extraction(string? total, params ExtractedInvoiceLine[] lines) =>
        new(null, null, "2026-10-10", total, false, lines);

    private static ExtractedInvoiceLine Line(string? name, string? quantity, string? unitPrice) =>
        new(name, null, quantity, unitPrice, null, null, null);

    private static AiAttempt Attempt(string model, AiCallOutcome outcome, int? status, int tokens = 0, decimal cost = 0m) =>
        new(model, outcome, status, tokens, tokens / 3, cost, 1500);

    private Task<Result<ProcessInvoiceOutcome>> Handle(Guid invoiceId) =>
        new ProcessInvoiceCommandHandler(
                _context.CurrentTenant,
                _context.Invoices,
                _context.Suppliers,
                _context.UsageRecords,
                _context.Storage,
                _context.AiCache,
                _context.Extractor,
                _context.OutputValidator,
                _context.InvoiceValidator(),
                _context.StatusCache,
                _context.UnitOfWork,
                _context.Clock)
            .Handle(new ProcessInvoiceCommand(invoiceId), CancellationToken.None);
}
