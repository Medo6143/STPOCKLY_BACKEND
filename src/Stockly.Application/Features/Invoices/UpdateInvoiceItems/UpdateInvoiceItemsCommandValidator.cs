using FluentValidation;
using Stockly.Application.Validation;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;
using Stockly.Domain.Text;

namespace Stockly.Application.Features.Invoices.UpdateInvoiceItems;

/// <summary>
/// I4 request shape (docs/design/backend.md §9.1 step 1–2): 1..100 lines with unique numbers 1..100, decimal strings,
/// <c>quantity &gt; 0</c>, names not blank after normalization, an existing product always with its unit, and never both a
/// product and a new product. A line with neither is <c>Invoice.LineProductRequired</c> from the handler. Precision
/// (<c>Precision.*</c>) and negative money are checked by the handler's parser.
/// </summary>
public sealed class UpdateInvoiceItemsCommandValidator : AbstractValidator<UpdateInvoiceItemsCommand>
{
    public UpdateInvoiceItemsCommandValidator()
    {
        RuleFor(c => c.Items).NotEmpty();
        When(c => c.Items is not null, () =>
        {
            RuleFor(c => c.Items!.Count).LessThanOrEqualTo(InvoiceLimits.MaxLines).OverridePropertyName(nameof(UpdateInvoiceItemsCommand.Items));
            RuleFor(c => c.Items).Must(HaveUniqueLineNumbers).WithMessage(_ => ValidationMessages.LineNumbersUnique);
        });
        RuleForEach(c => c.Items).NotNull().SetValidator(new LineValidator());
        RuleFor(c => c.SupplierName).MaximumLength(InvoiceLimits.SupplierNameMaxLength);
        RuleFor(c => c.InvoiceNumber).MaximumLength(InvoiceLimits.NumberMaxLength);
        RuleFor(c => c.TotalAmount).DecimalString();
    }

    /// <summary>Not blank once Arabic-normalized (a <c>null</c> value passes; required fields add <c>NotNull()</c>).</summary>
    private static bool HasText(string? text) => text is null || ArabicTextNormalizer.Normalize(text).Length > 0;

    private static bool HaveUniqueLineNumbers(IReadOnlyList<InvoiceLineRequest>? items)
    {
        var numbers = items!.Where(line => line is not null).Select(line => line.LineNo).ToList();
        return numbers.Distinct().Count() == numbers.Count;
    }

    // Shape only: a well-formed decimal string that is not negative and not zero. Precision is the handler's.
    private static bool IsPositive(string? text) =>
        !DecimalInput.IsWellFormed(text) || (text![0] != '-' && text.Any(digit => digit is >= '1' and <= '9'));

    private sealed class LineValidator : AbstractValidator<InvoiceLineRequest>
    {
        public LineValidator()
        {
            RuleFor(l => l.LineNo).InclusiveBetween(1, InvoiceLimits.MaxLines);
            RuleFor(l => l.RawName).NotNull().MaximumLength(InvoiceLimits.LineNameMaxLength)
                .Must(HasText).WithMessage(_ => ValidationMessages.TextRequired);
            RuleFor(l => l.Quantity).NotNull().DecimalString().Must(IsPositive).WithMessage(_ => ValidationMessages.MustBePositive);
            RuleFor(l => l.UnitPrice).NotNull().DecimalString();
            RuleFor(l => l.BatchNo).MaximumLength(InvoiceLimits.NumberMaxLength);
            RuleFor(l => l.ProductUnitId)
                .Must((line, unitId) => line.ProductId.HasValue == unitId.HasValue)
                .WithMessage(_ => ValidationMessages.ProductAndUnitTogether);
            RuleFor(l => l.NewProduct)
                .Must((line, newProduct) => newProduct is null || (line.ProductId is null && line.ProductUnitId is null))
                .WithMessage(_ => ValidationMessages.ProductOrNewProduct);
            RuleFor(l => l.NewProduct!).SetValidator(new NewProductValidator()).When(l => l.NewProduct is not null);
        }
    }

    private sealed class NewProductValidator : AbstractValidator<InvoiceNewProductDto>
    {
        public NewProductValidator()
        {
            RuleFor(p => p.Name).NotNull().Must(HasText).WithMessage(_ => ValidationMessages.TextRequired);
            RuleFor(p => p.BaseUnitName).NotNull().Must(HasText).WithMessage(_ => ValidationMessages.TextRequired);
            RuleFor(p => p.SellPrice).NotNull().DecimalString();
        }
    }
}
