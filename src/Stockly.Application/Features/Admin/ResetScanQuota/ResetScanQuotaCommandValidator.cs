using FluentValidation;

namespace Stockly.Application.Features.Admin.ResetScanQuota;

public sealed class ResetScanQuotaCommandValidator : AbstractValidator<ResetScanQuotaCommand>
{
    public ResetScanQuotaCommandValidator()
    {
        RuleFor(c => c.Reason).Reason();
    }
}
