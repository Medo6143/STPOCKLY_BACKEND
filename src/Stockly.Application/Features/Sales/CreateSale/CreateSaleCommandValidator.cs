using FluentValidation;
using Stockly.Application.Validation;
using Stockly.Contracts.Sales;
using Stockly.Domain.Sales;

namespace Stockly.Application.Features.Sales.CreateSale;

/// <summary>
/// S1 request shape: a client request id, 1..100 lines, each with a product, a unit, a well-formed quantity greater than
/// zero and an optional well-formed price; the note at most 500 characters. Precision (<c>Precision.*</c>) and negative
/// prices are checked by the handler's parser.
/// </summary>
public sealed class CreateSaleCommandValidator : AbstractValidator<CreateSaleCommand>
{
    public CreateSaleCommandValidator()
    {
        RuleFor(c => c.ClientRequestId).NotEmpty();
        RuleFor(c => c.Items).NotEmpty();
        When(c => c.Items is not null, () =>
            RuleFor(c => c.Items!.Count).LessThanOrEqualTo(SaleLimits.MaxItems).OverridePropertyName(nameof(CreateSaleCommand.Items)));
        RuleForEach(c => c.Items).NotNull().SetValidator(new ItemValidator());
        RuleFor(c => c.Note).MaximumLength(SaleLimits.NoteMaxLength);
    }

    // Shape only: a well-formed decimal string that is not negative and not zero. Precision is the handler's.
    private static bool IsPositive(string? text) =>
        !DecimalInput.IsWellFormed(text) || (text![0] != '-' && text.Any(digit => digit is >= '1' and <= '9'));

    private sealed class ItemValidator : AbstractValidator<SaleItemRequest>
    {
        public ItemValidator()
        {
            RuleFor(i => i.ProductId).NotEmpty();
            RuleFor(i => i.ProductUnitId).NotEmpty();
            RuleFor(i => i.Quantity).NotNull().DecimalString().Must(IsPositive).WithMessage(_ => ValidationMessages.MustBePositive);
            RuleFor(i => i.UnitPrice).DecimalString();
        }
    }
}
