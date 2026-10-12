using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Products.CountStock;

public sealed class CountStockCommandValidator : AbstractValidator<CountStockCommand>
{
    public CountStockCommandValidator()
    {
        RuleFor(c => c.CountedQuantity).NotNull().DecimalString();
    }
}
