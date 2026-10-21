using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Features.Invoices.ConfirmInvoice;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using Stockly.Domain.Suppliers;
using static Stockly.Application.Tests.Support.InvoiceTestContext;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>I5 handler (docs/design/backend.md §11). The concurrent and SQL parts: Api <c>ConfirmEndpointTests</c>.</summary>
public sealed class ConfirmInvoiceCommandHandlerTests
{
    private readonly InvoiceTestContext _context = new();
    private readonly IStockService _stock = Substitute.For<IStockService>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly List<Product> _added = [];
    private readonly List<ProductAlias> _aliases = [];
    private readonly InvoiceDetail _detail = new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Contracts.Invoices.InvoiceStatus.Confirmed, null, null, null, null, null, null, [], null, [], Now.UtcDateTime, null, Now.UtcDateTime);

    private readonly Product _panadol = Product.Create(ShopId, "بنادول", "شريط", 10m, 15m, 0m, Now).Value;
    private readonly ProductUnit _box;

    public ConfirmInvoiceCommandHandlerTests()
    {
        _box = _panadol.AddUnit("علبة", 3m, 40m, null).Value;
        _context.Queries.FindDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_detail);
        _context.Products.When(products => products.Add(Arg.Any<Product>())).Do(call => _added.Add(call.Arg<Product>()));
        _stock.LockProductsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().Contains(_panadol.Id)
                ? new Dictionary<Guid, Product> { [_panadol.Id] = _panadol }
                : new Dictionary<Guid, Product>());
        _stock.ReceiveFromInvoice(Arg.Any<Product>(), Arg.Any<decimal>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(call => call.Arg<Product>().RecordMovement(
                call.ArgAt<decimal>(1), MovementReason.InvoiceReceipt, MovementSource.FromInvoiceItem(call.ArgAt<Guid>(2), call.ArgAt<Guid>(3)), UserId, null, Now));
        _context.Suppliers.FindOrCreateAsync(Arg.Any<Supplier>(), Arg.Any<CancellationToken>())
            .Returns(call => new SupplierResolution(call.Arg<Supplier>().Id, Created: true));
        _context.Aliases.When(aliases => aliases.UpsertAsync(Arg.Any<IReadOnlyList<ProductAlias>>(), Arg.Any<CancellationToken>()))
            .Do(call => _aliases.AddRange(call.Arg<IReadOnlyList<ProductAlias>>()));
        _cache.When(cache => cache.IncrementVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => _context.UnitOfWork.CommitCount.Should().Be(1, "the version is bumped only after the commit"));
    }

    [Fact]
    public async Task Handle_ReviewedInvoice_ReceivesBaseQuantitiesCreatesProductConfirmsAndBumpsAfterCommit()
    {
        var invoice = Reviewed(" شركة الأدوية ");

        using var request = new CancellationTokenSource();
        var result = await Handle(invoice.Id, request.Token);

        result.Value.Should().BeSameAs(_detail);
        _stock.Received(1).ReceiveFromInvoice(_panadol, 6m, invoice.Id, Arg.Any<Guid>(), UserId);
        _added.Should().ContainSingle();
        var created = _added.Single();
        created.Name.Should().Be("فيتامين");
        created.CostPrice.Should().Be(30m, "the lowest line number sets the cost");
        created.BaseUnit.SellPrice.Should().Be(40m);
        created.Quantity.Should().Be(5m, "4 + 1 in its base unit");
        invoice.Status.Should().Be(DomainInvoiceStatus.Confirmed);
        invoice.SupplierId.Should().NotBeNull();
        invoice.Items.Where(item => item.NewProductName is not null).Should().OnlyContain(item => item.CreatedProductId == created.Id);
        _context.UnitOfWork.SaveCount.Should().Be(1);
        _context.UnitOfWork.CommitCount.Should().Be(1);
        _aliases.Select(alias => (alias.NormalizedAlias, alias.TimesConfirmed, alias.ProductId))
            .Should().BeEquivalentTo([("بنادول علبه", 1, _panadol.Id), ("فيتامين", 2, created.Id)]);
        await _cache.Received(1).IncrementVersionAsync(CacheKeys.CatalogVersion(ShopId), Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
        await _cache.Received(1).RemoveAsync(CacheKeys.SuppliersList(ShopId), Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
        await _context.StatusCache.Received(1).SetAsync(
            ShopId, Arg.Is<InvoiceStatusSnapshot>(snapshot => snapshot.Status == DomainInvoiceStatus.Confirmed), Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
    }

    [Fact]
    public async Task Handle_NoSupplierName_NoSupplierAndTheSupplierListIsKept()
    {
        var invoice = Reviewed(null);

        await Handle(invoice.Id);

        await _context.Suppliers.DidNotReceive().FindOrCreateAsync(Arg.Any<Supplier>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        invoice.SupplierId.Should().BeNull();
        _aliases.Should().OnlyContain(alias => alias.SupplierId == null);
    }

    [Fact]
    public async Task Handle_AlreadyConfirmed_ReturnsItWithoutWritesOrCacheWork()
    {
        var invoice = Reviewed(null);
        await Handle(invoice.Id);
        _cache.ClearReceivedCalls();
        _stock.ClearReceivedCalls();

        var repeat = await Handle(invoice.Id);

        repeat.Value.Should().BeSameAs(_detail);
        _stock.DidNotReceiveWithAnyArgs().ReceiveFromInvoice(default!, default, default, default, default);
        _context.UnitOfWork.SaveCount.Should().Be(1, "only the first confirm saved");
        await _cache.DidNotReceiveWithAnyArgs().IncrementVersionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_MissingOrOtherShopsInvoice_NotFound()
    {
        var result = await Handle(Guid.CreateVersion7());

        result.Error.Should().Be(InvoiceErrors.NotFound);
        await _cache.DidNotReceiveWithAnyArgs().IncrementVersionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ProcessingInvoice_NotConfirmable()
    {
        var invoice = _context.ProcessingInvoice();

        (await Handle(invoice.Id)).Error.Should().Be(InvoiceErrors.NotConfirmable);
        _context.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_UnmatchedLine_LineProductRequiredAndNothingWritten()
    {
        var invoice = _context.ProcessingInvoice();
        invoice.CompleteExtraction(InvoiceHeader.Empty, [new InvoiceLineDraft("x", 1m, 1m, null, null)], [], null, null, Now);

        var result = await Handle(invoice.Id);

        result.Error.Should().Be(InvoiceErrors.LineProductRequired);
        _context.UnitOfWork.SaveCount.Should().Be(0);
        await _stock.DidNotReceiveWithAnyArgs().LockProductsAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ConcurrentConfirmWonTheUniqueIndex_ReturnsTheConfirmedInvoiceWithoutCacheWork()
    {
        var invoice = Reviewed(null);
        _context.UnitOfWork.TrySaveResult = InvoiceErrors.AlreadyConfirmed;

        var result = await Handle(invoice.Id);

        result.Value.Should().BeSameAs(_detail);
        _context.UnitOfWork.CommitCount.Should().Be(0, "the failed attempt rolled back");
        await _cache.DidNotReceiveWithAnyArgs().IncrementVersionAsync(default!, default);
        await _context.StatusCache.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_OtherSaveFailure_ReturnedAndNotBumped()
    {
        var invoice = Reviewed(null);
        _context.UnitOfWork.TrySaveResult = ProductErrors.DuplicateBarcode;

        (await Handle(invoice.Id)).Error.Should().Be(ProductErrors.DuplicateBarcode);
        await _cache.DidNotReceiveWithAnyArgs().IncrementVersionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_NoUser_Unauthenticated()
    {
        _context.CurrentUser.UserId.Returns((Guid?)null);

        (await Handle(Guid.CreateVersion7())).Error.Code.Should().Be("Auth.Unauthenticated");
    }

    // NeedsReview after I4: 2 boxes of Panadol, and a new product on two lines (lowest line costs 30).
    private Invoice Reviewed(string? supplierName)
    {
        var invoice = _context.ProcessingInvoice();
        invoice.CompleteExtraction(InvoiceHeader.Empty, [new InvoiceLineDraft("x", 1m, 1m, null, null)], [], null, null, Now);
        invoice.ReplaceLines(
            new InvoiceHeader(supplierName?.Trim(), null, null, null),
            [
                new InvoiceReviewLine(1, "بنادول علبة", 2m, 100m, null, null, new InvoiceLineProduct(_panadol.Id, _box.Id), null),
                new InvoiceReviewLine(2, "فيتامين", 4m, 30m, null, null, null, new InvoiceNewProduct("فيتامين", "علبة", 40m)),
                new InvoiceReviewLine(3, "فيتامين", 1m, 35m, null, null, null, new InvoiceNewProduct("فيتامين", "علبه", 45m)),
            ],
            null,
            []).IsSuccess.Should().BeTrue();
        return invoice;
    }

    private Task<Result<InvoiceDetail>> Handle(Guid invoiceId, CancellationToken cancellationToken = default) =>
        new ConfirmInvoiceCommandHandler(
                _context.CurrentUser,
                _context.CurrentTenant,
                _context.Invoices,
                _context.Queries,
                _context.Products,
                _context.Suppliers,
                _context.Aliases,
                _stock,
                _context.UnitOfWork,
                _cache,
                _context.StatusCache,
                _context.Clock)
            .Handle(new ConfirmInvoiceCommand(invoiceId), cancellationToken);
}
