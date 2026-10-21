using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Features.Invoices.FailInvoiceProcessing;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using static Stockly.Application.Tests.Support.InvoiceTestContext;

namespace Stockly.Application.Tests.Features.Invoices;

public sealed class FailInvoiceProcessingCommandHandlerTests
{
    private readonly InvoiceTestContext _context = new();

    [Fact]
    public async Task Handle_ProcessingInvoice_FailedWithUnexpectedAndCached()
    {
        var invoice = _context.ProcessingInvoice();

        using var request = new CancellationTokenSource();
        var result = await Handle(invoice.Id, request.Token);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Failed);
        invoice.FailureReason.Should().Be(InvoiceFailureReason.Unexpected);
        _context.UnitOfWork.CommitCount.Should().Be(1);
        await _context.StatusCache.Received(1).SetAsync(
            ShopId, Arg.Is<InvoiceStatusSnapshot>(snapshot => snapshot.Status == InvoiceStatus.Failed), Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
    }

    [Fact]
    public async Task Handle_AlreadyExtracted_LeftUnchanged()
    {
        var invoice = _context.ProcessingInvoice();
        invoice.CompleteExtraction(InvoiceHeader.Empty, [new InvoiceLineDraft("x", 1m, 1m, null, null)], [], null, null, Now);

        var result = await Handle(invoice.Id);

        result.Error.Should().Be(InvoiceErrors.NotProcessing);
        invoice.Status.Should().Be(InvoiceStatus.NeedsReview);
        _context.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_OtherShopsInvoice_NotFound()
    {
        var result = await Handle(Guid.CreateVersion7());

        result.Error.Should().Be(InvoiceErrors.NotFound);
        await _context.StatusCache.DidNotReceive().SetAsync(Arg.Any<Guid>(), Arg.Any<InvoiceStatusSnapshot>(), Arg.Any<CancellationToken>());
    }

    private Task<Result> Handle(Guid invoiceId, CancellationToken cancellationToken = default) =>
        new FailInvoiceProcessingCommandHandler(_context.CurrentTenant, _context.Invoices, _context.StatusCache, _context.UnitOfWork, _context.Clock)
            .Handle(new FailInvoiceProcessingCommand(invoiceId), cancellationToken);
}
