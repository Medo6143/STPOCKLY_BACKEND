using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Products.AdjustStock;

/// <summary>Request shape (contract P9). A missing or blank note is <c>Stock.NoteRequired</c> from the Domain, not a shape error.</summary>
public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleFor(c => c.QuantityChange).NotNull().DecimalString();
        RuleFor(c => c.Note).MaximumLength(ProductLimits.AdjustmentNoteMaxLength);
    }
}
