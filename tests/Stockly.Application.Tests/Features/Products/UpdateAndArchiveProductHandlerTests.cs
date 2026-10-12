using NSubstitute;
using Stockly.Application.Features.Products.ArchiveProduct;
using Stockly.Application.Features.Products.UpdateProduct;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using static Stockly.Application.Tests.Support.ProductTestContext;

namespace Stockly.Application.Tests.Features.Products;

public sealed class UpdateAndArchiveProductHandlerTests
{
    private readonly ProductTestContext _context = new();

    [Fact]
    public async Task Update_ValidRequest_ReplacesDetailsSavesAndReturnsDetail()
    {
        var product = _context.Existing();

        var result = await UpdateHandler().Handle(Update(product.Id), CancellationToken.None);

        product.Name.Should().Be("Panadol Extra");
        product.Barcode.Should().Be("111");
        product.Category.Should().Be("مسكنات");
        product.CostPrice.Should().Be(9.75m);
        product.LowStockThreshold.Should().Be(2.5m);
        result.Value.Id.Should().Be(product.Id);
        await _context.UnitOfWork.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_BarcodeCheck_ExcludesTheProductItself()
    {
        var product = _context.Existing("111");

        await UpdateHandler().Handle(Update(product.Id), CancellationToken.None);

        await _context.Products.Received(1).IsBarcodeUsedAsync("111", product.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_BarcodeOfAnotherActiveProduct_ReturnsDuplicateBarcodeWithoutSaving()
    {
        var product = _context.Existing();
        _context.Products.IsBarcodeUsedAsync("111", product.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await UpdateHandler().Handle(Update(product.Id), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.DuplicateBarcode);
        await _context.UnitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_BarcodeTakenConcurrentlyAtSave_ReturnsDuplicateBarcode()
    {
        var product = _context.Existing();
        _context.UnitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(ProductErrors.DuplicateBarcode));

        var result = await UpdateHandler().Handle(Update(product.Id), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.DuplicateBarcode);
    }

    [Fact]
    public async Task Update_UnknownProduct_ReturnsNotFound()
    {
        var result = await UpdateHandler().Handle(Update(Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
    }

    [Fact]
    public async Task Update_OtherShopsProduct_ReturnsNotFound()
    {
        var result = await UpdateHandler().Handle(Update(OtherShopsProductId), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
    }

    [Fact]
    public async Task Update_ArchivedProduct_ReturnsArchived()
    {
        var product = _context.Existing();
        product.Archive();

        var result = await UpdateHandler().Handle(Update(product.Id), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.Archived);
    }

    [Theory]
    [InlineData("9.755", "1", "Precision.TooManyDecimals")]
    [InlineData("1", "-1", "Precision.Negative")]
    public async Task Update_InvalidDecimal_ReturnsPrecisionErrorBeforeLoading(string costPrice, string threshold, string code)
    {
        var result = await UpdateHandler().Handle(
            Update(Guid.CreateVersion7()) with { CostPrice = costPrice, LowStockThreshold = threshold }, CancellationToken.None);

        result.Error.Code.Should().Be(code);
        await _context.Products.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Archive_ExistingProduct_ArchivesAndSaves()
    {
        var product = _context.Existing();

        var result = await ArchiveHandler().Handle(new ArchiveProductCommand(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.IsArchived.Should().BeTrue();
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Archive_AlreadyArchived_IsIdempotent()
    {
        var product = _context.Existing();
        product.Archive();

        var result = await ArchiveHandler().Handle(new ArchiveProductCommand(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Archive_OtherShopsProduct_ReturnsNotFound()
    {
        var result = await ArchiveHandler().Handle(new ArchiveProductCommand(OtherShopsProductId), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
        await _context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static UpdateProductCommand Update(Guid productId) =>
        new(productId, "Panadol Extra", "9.75", "2.500", " 111 ", "مسكنات");

    private UpdateProductCommandHandler UpdateHandler() => new(_context.Products, _context.Queries, _context.UnitOfWork);

    private ArchiveProductCommandHandler ArchiveHandler() => new(_context.Products, _context.UnitOfWork);
}
