using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Products.CreateProduct;

/// <summary>
/// Request shape (contract P3). Blank names and precision rules are not shape errors: they return
/// <c>Product.NameRequired</c>, <c>ProductUnit.NameRequired</c> and <c>Precision.*</c> from the handler.
/// </summary>
public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(c => c.Name).NotNull();
        RuleFor(c => c.BaseUnitName).NotNull();
        RuleFor(c => c.CostPrice).NotNull().DecimalString();
        RuleFor(c => c.BaseUnitSellPrice).NotNull().DecimalString();
        RuleFor(c => c.LowStockThreshold).DecimalString();
        RuleFor(c => c.OpeningQuantity).DecimalString();
        RuleFor(c => c.Barcode).MaximumLength(ProductLimits.BarcodeMaxLength);
        RuleFor(c => c.Category).MaximumLength(ProductLimits.CategoryMaxLength);
    }
}
