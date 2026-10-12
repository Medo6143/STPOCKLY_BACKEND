using NSubstitute;
using Stockly.Application.Features.Products.AddProductUnit;
using Stockly.Application.Features.Products.RemoveProductUnit;
using Stockly.Application.Features.Products.UpdateProductUnit;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using static Stockly.Application.Tests.Support.ProductTestContext;

namespace Stockly.Application.Tests.Features.Products;

/// <summary>P6–P8 handlers.</summary>
public sealed class ProductUnitHandlerTests
{
    private readonly ProductTestContext _context = new();

    [Fact]
    public async Task Add_ValidUnit_AddsSavesAndReturnsFormattedDto()
    {
        var product = _context.Existing();

        var result = await AddHandler().Handle(Add(product.Id), CancellationToken.None);

        result.Value.Name.Should().Be("علبة");
        result.Value.Factor.Should().Be("3.000000");
        result.Value.SellPrice.Should().Be("40.00");
        result.Value.Barcode.Should().Be("222");
        result.Value.IsBase.Should().BeFalse();
        product.Units.Should().HaveCount(2);
        await _context.UnitOfWork.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_BarcodeUsedByAUnitOfTheShop_ReturnsDuplicateBarcodeWithoutSaving()
    {
        var product = _context.Existing();
        _context.Products.IsUnitBarcodeUsedAsync("222", Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await AddHandler().Handle(Add(product.Id), CancellationToken.None);

        result.Error.Should().Be(ProductUnitErrors.DuplicateBarcode);
        await _context.UnitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAndUpdate_BarcodeTakenConcurrentlyAtSave_ReturnDuplicateBarcode()
    {
        var product = _context.Existing();
        var box = product.AddUnit("كرتونة", 30m, 400m, null).Value;
        _context.UnitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(ProductUnitErrors.DuplicateBarcode));

        (await AddHandler().Handle(Add(product.Id), CancellationToken.None)).Error.Should().Be(ProductUnitErrors.DuplicateBarcode);
        (await UpdateHandler().Handle(Update(product.Id, box.Id), CancellationToken.None)).Error.Should().Be(ProductUnitErrors.DuplicateBarcode);
    }

    [Theory]
    [InlineData("0", "40", "Precision.NotPositive")]
    [InlineData("1.0000001", "40", "Precision.TooManyDecimals")]
    [InlineData("3", "-1", "Precision.Negative")]
    [InlineData("100000000", "40", "Precision.OutOfRange")]
    public async Task Add_InvalidDecimal_ReturnsPrecisionError(string factor, string sellPrice, string code)
    {
        var product = _context.Existing();

        var result = await AddHandler().Handle(Add(product.Id) with { Factor = factor, SellPrice = sellPrice }, CancellationToken.None);

        result.Error.Code.Should().Be(code);
    }

    [Fact]
    public async Task Add_DuplicateNameOrArchived_ReturnsDomainErrors()
    {
        var product = _context.Existing();

        (await AddHandler().Handle(Add(product.Id) with { Name = "شَريـط" }, CancellationToken.None))
            .Error.Should().Be(ProductUnitErrors.DuplicateName);

        product.Archive();
        (await AddHandler().Handle(Add(product.Id), CancellationToken.None)).Error.Should().Be(ProductErrors.Archived);
    }

    [Fact]
    public async Task Add_OtherShopsProduct_ReturnsNotFound()
    {
        var result = await AddHandler().Handle(Add(OtherShopsProductId), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
    }

    [Fact]
    public async Task Update_ValidUnit_AppliesAndChecksBarcodeExceptItself()
    {
        var product = _context.Existing();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        var result = await UpdateHandler().Handle(Update(product.Id, box.Id), CancellationToken.None);

        result.Value.Factor.Should().Be("2.500000");
        box.Name.Should().Be("كرتونة");
        await _context.Products.Received(1).IsUnitBarcodeUsedAsync("333", box.Id, Arg.Any<CancellationToken>());
        await _context.UnitOfWork.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_BaseUnitFactorChanged_ReturnsBaseUnitFactorFixed()
    {
        var product = _context.Existing();

        var result = await UpdateHandler().Handle(Update(product.Id, product.BaseUnit.Id), CancellationToken.None);

        result.Error.Should().Be(ProductUnitErrors.BaseUnitFactorFixed);
    }

    [Fact]
    public async Task Update_UnknownUnit_ReturnsUnitNotFound()
    {
        var product = _context.Existing();

        var result = await UpdateHandler().Handle(Update(product.Id, Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(ProductUnitErrors.NotFound);
    }

    [Fact]
    public async Task Update_BarcodeUsedInTheShop_ReturnsDuplicateBarcode()
    {
        var product = _context.Existing();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;
        _context.Products.IsUnitBarcodeUsedAsync("333", box.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await UpdateHandler().Handle(Update(product.Id, box.Id), CancellationToken.None);

        result.Error.Should().Be(ProductUnitErrors.DuplicateBarcode);
    }

    [Fact]
    public async Task Update_OtherShopsProduct_ReturnsNotFound()
    {
        var result = await UpdateHandler().Handle(Update(OtherShopsProductId, Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
    }

    [Fact]
    public async Task Remove_NonBaseUnit_RemovesAndSaves()
    {
        var product = _context.Existing();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        var result = await RemoveHandler().Handle(new RemoveProductUnitCommand(product.Id, box.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Units.Should().ContainSingle();
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_BaseUnit_ReturnsBaseUnitCannotBeRemoved()
    {
        var product = _context.Existing();

        var result = await RemoveHandler().Handle(new RemoveProductUnitCommand(product.Id, product.BaseUnit.Id), CancellationToken.None);

        result.Error.Should().Be(ProductUnitErrors.BaseUnitCannotBeRemoved);
        await _context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_UnknownUnitOrArchivedProduct_ReturnsDomainErrors()
    {
        var product = _context.Existing();

        (await RemoveHandler().Handle(new RemoveProductUnitCommand(product.Id, Guid.CreateVersion7()), CancellationToken.None))
            .Error.Should().Be(ProductUnitErrors.NotFound);

        product.Archive();
        (await RemoveHandler().Handle(new RemoveProductUnitCommand(product.Id, Guid.CreateVersion7()), CancellationToken.None))
            .Error.Should().Be(ProductErrors.Archived);
    }

    [Fact]
    public async Task Remove_OtherShopsProduct_ReturnsNotFound()
    {
        var result = await RemoveHandler().Handle(new RemoveProductUnitCommand(OtherShopsProductId, Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.NotFound);
    }

    private static AddProductUnitCommand Add(Guid productId) => new(productId, "علبة", "3.000000", "40.00", " 222 ");

    private static UpdateProductUnitCommand Update(Guid productId, Guid unitId) => new(productId, unitId, "كرتونة", "2.5", "35", "333");

    private AddProductUnitCommandHandler AddHandler() => new(_context.Products, _context.UnitOfWork);

    private UpdateProductUnitCommandHandler UpdateHandler() => new(_context.Products, _context.UnitOfWork);

    private RemoveProductUnitCommandHandler RemoveHandler() => new(_context.Products, _context.UnitOfWork);
}
