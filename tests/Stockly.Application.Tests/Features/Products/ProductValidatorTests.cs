using FluentValidation;
using Stockly.Application.Features.Products.AddProductUnit;
using Stockly.Application.Features.Products.AdjustStock;
using Stockly.Application.Features.Products.CountStock;
using Stockly.Application.Features.Products.CreateProduct;
using Stockly.Application.Features.Products.GetLowStock;
using Stockly.Application.Features.Products.GetProductMovements;
using Stockly.Application.Features.Products.GetProducts;
using Stockly.Application.Features.Products.UpdateProduct;
using Stockly.Application.Features.Products.UpdateProductUnit;
using Stockly.Application.Validation;

namespace Stockly.Application.Tests.Features.Products;

/// <summary>Request shape of P1–P11: failures become <c>400 Validation.Failed</c> with these field keys.</summary>
public sealed class ProductValidatorTests
{
    [Fact]
    public void Create_ValidRequest_HasNoErrors()
    {
        Errors(new CreateProductCommandValidator(), Create()).Should().BeEmpty();
    }

    [Fact]
    public void Create_MissingRequiredFields_FailsEachOne()
    {
        var command = new CreateProductCommand(null!, null!, null!, null!, null, null, null, null);

        Errors(new CreateProductCommandValidator(), command)
            .Should().BeEquivalentTo("name", "baseUnitName", "costPrice", "baseUnitSellPrice");
    }

    [Fact]
    public void Create_MalformedDecimalsAndLongTexts_FailEachField()
    {
        var command = Create() with
        {
            CostPrice = "12,50",
            BaseUnitSellPrice = "1e3",
            LowStockThreshold = " 1",
            OpeningQuantity = "abc",
            Barcode = new string('1', 65),
            Category = new string('x', 101),
        };

        Errors(new CreateProductCommandValidator(), command).Should().BeEquivalentTo(
            "costPrice", "baseUnitSellPrice", "lowStockThreshold", "openingQuantity", "barcode", "category");
    }

    [Fact]
    public void Create_BlankNameAndPrecisionProblems_AreNotShapeErrors()
    {
        var command = Create() with { Name = " ", BaseUnitName = "", CostPrice = "1.999", LowStockThreshold = "-1" };

        Errors(new CreateProductCommandValidator(), command).Should().BeEmpty("the handler returns their own codes");
    }

    [Fact]
    public void Create_DecimalFormatMessage_IsTheStocklyMessage()
    {
        var result = new CreateProductCommandValidator().Validate(Create() with { CostPrice = "x" });

        result.Errors.Single().ErrorMessage.Should().Be(ValidationMessages.DecimalFormat);
    }

    [Fact]
    public void Update_MissingRequiredFields_FailEachOne()
    {
        var command = new UpdateProductCommand(Guid.CreateVersion7(), null!, null!, null!, null, null);

        Errors(new UpdateProductCommandValidator(), command).Should().BeEquivalentTo("name", "costPrice", "lowStockThreshold");
    }

    [Fact]
    public void Update_Valid_HasNoErrors()
    {
        var command = new UpdateProductCommand(Guid.CreateVersion7(), "n", "1", "0", null, null);

        Errors(new UpdateProductCommandValidator(), command).Should().BeEmpty();
    }

    [Fact]
    public void Units_MissingOrMalformedFields_FailEachOne()
    {
        Errors(new AddProductUnitCommandValidator(), new AddProductUnitCommand(Guid.CreateVersion7(), null!, "x", null!, null))
            .Should().BeEquivalentTo("name", "factor", "sellPrice");
        Errors(new UpdateProductUnitCommandValidator(), new UpdateProductUnitCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "n", "1", "40", null))
            .Should().BeEmpty();
    }

    [Fact]
    public void Adjust_ShapeRules()
    {
        var validator = new AdjustStockCommandValidator();

        Errors(validator, new AdjustStockCommand(Guid.CreateVersion7(), "-2.000", null, null)).Should().BeEmpty("a missing note is Stock.NoteRequired");
        Errors(validator, new AdjustStockCommand(Guid.CreateVersion7(), null!, null, null)).Should().Equal("quantityChange");
        Errors(validator, new AdjustStockCommand(Guid.CreateVersion7(), "1", new string('n', 501), null)).Should().Equal("note");
        Errors(validator, new AdjustStockCommand(Guid.CreateVersion7(), "1", new string('n', 500), null)).Should().BeEmpty();
    }

    [Fact]
    public void Count_ShapeRules()
    {
        var validator = new CountStockCommandValidator();

        Errors(validator, new CountStockCommand(Guid.CreateVersion7(), "26.000", null)).Should().BeEmpty();
        Errors(validator, new CountStockCommand(Guid.CreateVersion7(), "26,0", null)).Should().Equal("countedQuantity");
    }

    [Theory]
    [InlineData(1, 100, 0)]
    [InlineData(0, 20, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 101, 1)]
    public void Paging_PageAtLeastOneAndPageSizeUpTo100(int page, int pageSize, int errorCount)
    {
        Errors(new GetProductsQueryValidator(), new GetProductsQuery(null, false, page, pageSize)).Should().HaveCount(errorCount);
        Errors(new GetLowStockQueryValidator(), new GetLowStockQuery(null, false, page, pageSize)).Should().HaveCount(errorCount);
        Errors(new GetProductMovementsQueryValidator(), new GetProductMovementsQuery(Guid.CreateVersion7(), page, pageSize))
            .Should().HaveCount(errorCount);
    }

    [Fact]
    public void Search_LongerThan100_Fails()
    {
        Errors(new GetProductsQueryValidator(), new GetProductsQuery(new string('a', 101), false, 1, 20)).Should().Equal("search");
        Errors(new GetProductsQueryValidator(), new GetProductsQuery(new string('a', 100), false, 1, 20)).Should().BeEmpty();
    }

    private static CreateProductCommand Create() =>
        new("بنادول اكسترا", "شريط", "12.50", "15.00", "6221234567890", "مسكنات", "5.000", "24.000");

    private static List<string> Errors<T>(IValidator<T> validator, T instance) =>
        validator.Validate(instance).Errors.Select(error => FieldPath.ToCamelCase(error.PropertyName)).Distinct().ToList();
}
