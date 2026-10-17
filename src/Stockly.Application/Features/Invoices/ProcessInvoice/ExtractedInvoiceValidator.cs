using FluentValidation;
using Stockly.Application.Abstractions.Ai;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.ProcessInvoice;

/// <summary>
/// Structural limits of untrusted model output (AGENTS.md rule 15, docs/design/backend.md §7.1). An output that fails
/// is <c>InvalidOutput</c>. Formats of numbers and dates are not checked here: the normalizer drops what it cannot read.
/// </summary>
public sealed class ExtractedInvoiceValidator : AbstractValidator<ExtractedInvoice>
{
    /// <summary>Longest number or date string accepted from the model.</summary>
    public const int ValueMaxLength = 32;

    /// <summary>Longest unit text accepted from the model.</summary>
    public const int UnitMaxLength = 50;

    public ExtractedInvoiceValidator()
    {
        RuleFor(invoice => invoice.SupplierName).MaximumLength(InvoiceLimits.SupplierNameMaxLength);
        RuleFor(invoice => invoice.InvoiceNumber).MaximumLength(InvoiceLimits.NumberMaxLength);
        RuleFor(invoice => invoice.InvoiceDate).MaximumLength(ValueMaxLength);
        RuleFor(invoice => invoice.TotalAmount).MaximumLength(ValueMaxLength);
        RuleFor(invoice => invoice.Items).NotNull().Must(items => items.Count <= InvoiceLimits.MaxLines);
        RuleForEach(invoice => invoice.Items).NotNull().ChildRules(line =>
        {
            line.RuleFor(item => item.Name).MaximumLength(InvoiceLimits.LineNameMaxLength);
            line.RuleFor(item => item.Unit).MaximumLength(UnitMaxLength);
            line.RuleFor(item => item.Quantity).MaximumLength(ValueMaxLength);
            line.RuleFor(item => item.UnitPrice).MaximumLength(ValueMaxLength);
            line.RuleFor(item => item.LineTotal).MaximumLength(ValueMaxLength);
            line.RuleFor(item => item.ExpiryDate).MaximumLength(ValueMaxLength);
            line.RuleFor(item => item.BatchNo).MaximumLength(InvoiceLimits.NumberMaxLength);
        });
    }
}
