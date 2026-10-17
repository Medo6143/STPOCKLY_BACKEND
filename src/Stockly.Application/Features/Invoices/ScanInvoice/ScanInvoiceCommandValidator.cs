using FluentValidation;

namespace Stockly.Application.Features.Invoices.ScanInvoice;

/// <summary>Request shape (contract I1): <c>clientRequestId</c> is a non-empty UUID. The image is checked by the handler.</summary>
public sealed class ScanInvoiceCommandValidator : AbstractValidator<ScanInvoiceCommand>
{
    public ScanInvoiceCommandValidator()
    {
        RuleFor(command => command.ClientRequestId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(value => Guid.TryParse(value, out var id) && id != Guid.Empty);
    }
}
