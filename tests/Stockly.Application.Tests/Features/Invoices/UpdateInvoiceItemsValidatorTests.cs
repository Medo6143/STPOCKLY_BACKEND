using Stockly.Application.Features.Invoices.UpdateInvoiceItems;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;
using static Stockly.Application.Tests.Features.Invoices.UpdateInvoiceItemsCommandHandlerTests;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>I4 request shape: every failure is <c>400 Validation.Failed</c> with the camelCase field path (contract §1.8).</summary>
public sealed class UpdateInvoiceItemsValidatorTests
{
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly Guid UnitId = Guid.CreateVersion7();
    private readonly UpdateInvoiceItemsCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidLines_Passes()
    {
        var result = _validator.Validate(Command(
            Guid.CreateVersion7(), [Existing(1, ProductId, UnitId, "1.5", "0"), New(2, "جديد", "علبة", "1.00", "2", "3.00")], totalAmount: "4.50"));

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    public static TheoryData<string, InvoiceLineRequest> InvalidLines() => new()
    {
        { "LineNo", Existing(0, ProductId, UnitId, "1", "1.00") },
        { "LineNo", Existing(InvoiceLimits.MaxLines + 1, ProductId, UnitId, "1", "1.00") },
        { "Quantity", Existing(1, ProductId, UnitId, "0", "1.00") },
        { "Quantity", Existing(1, ProductId, UnitId, "0.000", "1.00") },
        { "Quantity", Existing(1, ProductId, UnitId, "-1", "1.00") },
        { "Quantity", Existing(1, ProductId, UnitId, "abc", "1.00") },
        { "UnitPrice", Existing(1, ProductId, UnitId, "1", "1,00") },
        { "RawName", Existing(1, ProductId, UnitId, "1", "1.00") with { RawName = " ـ " } },
        { "RawName", Existing(1, ProductId, UnitId, "1", "1.00") with { RawName = null! } },
        { "ProductUnitId", Existing(1, ProductId, UnitId, "1", "1.00") with { ProductUnitId = null } },
        { "ProductUnitId", Existing(1, ProductId, UnitId, "1", "1.00") with { ProductId = null } },
        { "NewProduct", New(1, "جديد", "علبة", "1.00", "1", "1.00") with { ProductId = ProductId, ProductUnitId = UnitId } },
        { "NewProduct.Name", New(1, " ", "علبة", "1.00", "1", "1.00") },
        { "NewProduct.BaseUnitName", New(1, "جديد", "", "1.00", "1", "1.00") },
        { "NewProduct.SellPrice", New(1, "جديد", "علبة", "x", "1", "1.00") },
        { "BatchNo", Existing(1, ProductId, UnitId, "1", "1.00") with { BatchNo = new string('b', InvoiceLimits.NumberMaxLength + 1) } },
    };

    [Theory]
    [MemberData(nameof(InvalidLines))]
    public void Validate_InvalidLine_FailsOnThatField(string field, InvoiceLineRequest line)
    {
        var result = _validator.Validate(Command(Guid.CreateVersion7(), [line]));

        result.Errors.Select(error => error.PropertyName).Should().Contain($"Items[0].{field}");
    }

    [Fact]
    public void Validate_NoItems_Fails()
    {
        _validator.Validate(Command(Guid.CreateVersion7(), [])).Errors.Should().Contain(error => error.PropertyName == "Items");
        _validator.Validate(Command(Guid.CreateVersion7(), null!)).Errors.Should().Contain(error => error.PropertyName == "Items");
    }

    [Fact]
    public void Validate_MoreThanHundredLines_Fails()
    {
        var lines = Enumerable.Range(1, InvoiceLimits.MaxLines + 1).Select(i => Existing(i, ProductId, UnitId, "1", "1.00")).ToList();

        _validator.Validate(Command(Guid.CreateVersion7(), lines)).Errors.Should().Contain(error => error.PropertyName == "Items");
    }

    [Fact]
    public void Validate_DuplicateLineNumbers_Fails()
    {
        var result = _validator.Validate(Command(
            Guid.CreateVersion7(), [Existing(1, ProductId, UnitId, "1", "1.00"), Existing(1, ProductId, UnitId, "1", "1.00")]));

        result.Errors.Should().Contain(error => error.PropertyName == "Items");
    }

    [Fact]
    public void Validate_HeaderTooLongOrMalformed_Fails()
    {
        var command = Command(Guid.CreateVersion7(), [Existing(1, ProductId, UnitId, "1", "1.00")], totalAmount: "1e3") with
        {
            SupplierName = new string('s', InvoiceLimits.SupplierNameMaxLength + 1),
            InvoiceNumber = new string('n', InvoiceLimits.NumberMaxLength + 1),
        };

        _validator.Validate(command).Errors.Select(error => error.PropertyName)
            .Should().Contain(["SupplierName", "InvoiceNumber", "TotalAmount"]);
    }

    [Fact]
    public void Validate_LineWithNeitherProductNorNewProduct_PassesShapeForTheHandlersLineProductRequired()
    {
        _validator.Validate(Command(Guid.CreateVersion7(), [new InvoiceLineRequest(1, "x", "1", "1.00")])).IsValid.Should().BeTrue();
    }
}
