using FluentValidation;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.LookupProduct;

/// <summary>P12: a barcode is required (<c>400 Validation.Failed</c> when missing or blank) and at most 64 characters.</summary>
public sealed class LookupProductQueryValidator : AbstractValidator<LookupProductQuery>
{
    public LookupProductQueryValidator()
    {
        RuleFor(q => q.Barcode).NotEmpty().MaximumLength(Product.BarcodeMaxLength);
    }
}
