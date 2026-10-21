using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Features.Sales.CreateSale;
using Stockly.Application.Features.Sales.GetSale;
using Stockly.Application.Features.Sales.GetSales;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Sales;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Sales;
using Stockly.Domain.Stock;
using static Stockly.Application.Tests.Support.InvoiceTestContext;

namespace Stockly.Application.Tests.Features.Sales;

/// <summary>S1–S3 handlers and validators (contract §7). The concurrent and SQL parts: Api <c>SalesEndpointTests</c>.</summary>
public sealed class CreateSaleCommandHandlerTests
{
    private readonly InvoiceTestContext _context = new();
    private readonly ISaleRepository _sales = Substitute.For<ISaleRepository>();
    private readonly ISaleQueries _queries = Substitute.For<ISaleQueries>();
    private readonly IStockService _stock = Substitute.For<IStockService>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly List<Sale> _added = [];
    private readonly SaleDto _dto = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "0.00", null, false, [], Now.UtcDateTime, UserId, []);
    private readonly Product _product = Product.Create(ShopId, "بنادول", "شريط", 10m, 15m, 0m, Now).Value;
    private readonly ProductUnit _box;

    public CreateSaleCommandHandlerTests()
    {
        _box = _product.AddUnit("علبة", 3m, 40m, null).Value;
        _sales.When(sales => sales.Add(Arg.Any<Sale>())).Do(call => _added.Add(call.Arg<Sale>()));
        _queries.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_dto);
        _stock.LockProductsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().Contains(_product.Id)
                ? new Dictionary<Guid, Product> { [_product.Id] = _product }
                : new Dictionary<Guid, Product>());
        _stock.RecordSale(Arg.Any<Product>(), Arg.Any<decimal>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(call => call.Arg<Product>().RecordMovement(
                -call.ArgAt<decimal>(1), MovementReason.Sale, MovementSource.FromSaleItem(call.ArgAt<Guid>(2), call.ArgAt<Guid>(3)), UserId, null, Now));
        _cache.When(cache => cache.IncrementVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => _context.UnitOfWork.CommitCount.Should().Be(1, "the version is bumped only after the commit"));
    }

    [Fact]
    public async Task Handle_NewSale_HeaderFirstThenLinesAndStockInBaseUnitsAndBumpAfterCommit()
    {
        using var request = new CancellationTokenSource();
        var result = await Handle(Command(Line(_box.Id, "2"), Line(_product.BaseUnit.Id, "1.5", "14.25")), request.Token);

        result.Value.Created.Should().BeTrue();
        result.Value.Sale.Should().BeSameAs(_dto);
        var sale = _added.Single();
        sale.Items.Select(item => (item.LineNo, item.ListUnitPrice, item.UnitPrice, item.LineTotal))
            .Should().Equal((1, 40m, 40m, 80m), (2, 15m, 14.25m, 21.38m));
        sale.TotalAmount.Should().Be(101.38m);
        _stock.Received(1).RecordSale(_product, 6m, sale.Id, Arg.Any<Guid>(), UserId);
        _stock.Received(1).RecordSale(_product, 1.5m, sale.Id, Arg.Any<Guid>(), UserId);
        _product.Quantity.Should().Be(-7.5m, "negative stock never blocks");
        _context.UnitOfWork.SaveCount.Should().Be(2, "the header is saved before the products are locked");
        await _cache.Received(1).IncrementVersionAsync(CacheKeys.CatalogVersion(ShopId), Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
    }

    [Fact]
    public async Task Handle_RepeatClientRequestId_ExistingSaleWithoutTransaction()
    {
        var command = Command(Line(_box.Id, "1"));
        _queries.FindByClientRequestIdAsync(command.ClientRequestId, Arg.Any<CancellationToken>()).Returns(_dto);

        var result = await Handle(command);

        result.Value.Should().Be(new CreateSaleResult(_dto, Created: false));
        _added.Should().BeEmpty();
        await _cache.DidNotReceiveWithAnyArgs().IncrementVersionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ConcurrentRepeatWonTheInsert_ReturnsTheWinnerWithoutBump()
    {
        var command = Command(Line(_box.Id, "1"));
        _context.UnitOfWork.TrySaveResult = SaleErrors.DuplicateClientRequest;
        _queries.FindByClientRequestIdAsync(command.ClientRequestId, Arg.Any<CancellationToken>()).Returns(null, _dto);

        var result = await Handle(command);

        result.Value.Should().Be(new CreateSaleResult(_dto, Created: false));
        _context.UnitOfWork.CommitCount.Should().Be(0);
        await _stock.DidNotReceiveWithAnyArgs().LockProductsAsync(default!, default);
        await _cache.DidNotReceiveWithAnyArgs().IncrementVersionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_MissingOrOtherShopsProduct_ProductNotFoundAndRolledBack()
    {
        var result = await Handle(Command(new SaleItemRequest(Guid.CreateVersion7(), _box.Id, "1")));

        result.Error.Should().Be(ProductErrors.NotFound);
        _context.UnitOfWork.CommitCount.Should().Be(0);
        await _cache.DidNotReceiveWithAnyArgs().IncrementVersionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_UnitOfAnotherProduct_UnitNotOfProduct_UnknownUnit_ProductUnitNotFound()
    {
        var otherUnit = Guid.CreateVersion7();
        _context.Products.UnitExistsAsync(otherUnit, Arg.Any<CancellationToken>()).Returns(true);

        (await Handle(Command(Line(otherUnit, "1")))).Error.Should().Be(SaleErrors.UnitNotOfProduct);
        (await Handle(Command(Line(Guid.CreateVersion7(), "1")))).Error.Should().Be(ProductUnitErrors.NotFound);
    }

    [Fact]
    public async Task Handle_ArchivedProduct_SaleArchivedProduct()
    {
        _product.Archive();

        (await Handle(Command(Line(_box.Id, "1")))).Error.Should().Be(SaleErrors.ArchivedProduct);
        _product.Quantity.Should().Be(0m);
    }

    [Theory]
    [InlineData("1.0005", null, "Precision.TooManyDecimals", "items[0].quantity")]
    [InlineData("1", "-1.00", "Precision.Negative", "items[0].unitPrice")]
    [InlineData("1", "1.005", "Precision.TooManyDecimals", "items[0].unitPrice")]
    public async Task Handle_Precision_RejectedBeforeAnyWrite(string quantity, string? price, string code, string field)
    {
        var result = await Handle(Command(Line(_box.Id, quantity, price)));

        result.Error.Code.Should().Be(code);
        result.Error.Message.Should().Contain(field);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoUser_Unauthenticated()
    {
        _context.CurrentUser.UserId.Returns((Guid?)null);

        (await Handle(Command(Line(_box.Id, "1")))).Error.Code.Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task Validator_RequestShape()
    {
        var validator = new CreateSaleCommandValidator();
        var line = Line(_box.Id, "1");

        (await validator.ValidateAsync(Command(line))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(Command(line) with { ClientRequestId = Guid.Empty })).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Command() with { Items = [] })).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Command([.. Enumerable.Repeat(line, SaleLimits.MaxItems + 1)]))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Command(Line(_box.Id, "0")))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Command(Line(_box.Id, "-1")))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Command(Line(_box.Id, "1", "abc")))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Command(new SaleItemRequest(Guid.Empty, _box.Id, "1")))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Command(line) with { Note = new string('x', SaleLimits.NoteMaxLength + 1) })).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetSale_MissingOrOtherShops_SaleNotFound_Found_ReturnsIt()
    {
        var handler = new GetSaleQueryHandler(_queries);
        _queries.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SaleDto?)null);
        _queries.FindAsync(_dto.Id, Arg.Any<CancellationToken>()).Returns(_dto);

        (await handler.Handle(new GetSaleQuery(Guid.CreateVersion7()), CancellationToken.None)).Error.Should().Be(SaleErrors.NotFound);
        (await handler.Handle(new GetSaleQuery(_dto.Id), CancellationToken.None)).Value.Should().BeSameAs(_dto);
    }

    [Fact]
    public async Task GetSales_PassesTheRangeInUtcAndValidatesIt()
    {
        var from = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(3));
        var to = from.AddHours(1);
        var validator = new GetSalesQueryValidator();

        await new GetSalesQueryHandler(_queries).Handle(new GetSalesQuery(from, to, 2, 10), CancellationToken.None);

        await _queries.Received(1).ListAsync(
            Arg.Is<DateTimeOffset?>(value => value!.Value.Offset == TimeSpan.Zero && value.Value == from), to.ToUniversalTime(), 2, 10, Arg.Any<CancellationToken>());
        (await validator.ValidateAsync(new GetSalesQuery(to, from, 1, 20))).IsValid.Should().BeFalse("the range ends before it starts");
        (await validator.ValidateAsync(new GetSalesQuery(null, null, 1, 101))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new GetSalesQuery(null, null, 0, 20))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new GetSalesQuery(from, from, 1, 100))).IsValid.Should().BeTrue();
    }

    private static CreateSaleCommand Command(params SaleItemRequest[] items) => new(Guid.CreateVersion7(), items, null);

    private SaleItemRequest Line(Guid unitId, string quantity, string? unitPrice = null) => new(_product.Id, unitId, quantity, unitPrice);

    private Task<Result<CreateSaleResult>> Handle(CreateSaleCommand command, CancellationToken cancellationToken = default) =>
        new CreateSaleCommandHandler(
                _context.CurrentUser, _context.CurrentTenant, _sales, _queries, _context.Products, _stock, _context.UnitOfWork, _cache, _context.Clock)
            .Handle(command, cancellationToken);
}
