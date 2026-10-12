using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Products.UpdateProduct;

/// <summary>Request shape (contract P4). Blank names and precision rules return their own codes from the handler.</summary>
public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(c => c.Name).NotNull();
        RuleFor(c => c.CostPrice).NotNull().DecimalString();
        RuleFor(c => c.LowStockThreshold).NotNull().DecimalString();
        RuleFor(c => c.Barcode).MaximumLength(ProductLimits.BarcodeMaxLength);
        RuleFor(c => c.Category).MaximumLength(ProductLimits.CategoryMaxLength);
    }
}
