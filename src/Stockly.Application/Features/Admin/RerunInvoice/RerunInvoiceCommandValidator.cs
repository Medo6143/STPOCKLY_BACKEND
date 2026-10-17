using FluentValidation;

namespace Stockly.Application.Features.Admin.RerunInvoice;

public sealed class RerunInvoiceCommandValidator : AbstractValidator<RerunInvoiceCommand>
{
    public RerunInvoiceCommandValidator()
    {
        RuleFor(c => c.Reason).Reason();
    }
}
