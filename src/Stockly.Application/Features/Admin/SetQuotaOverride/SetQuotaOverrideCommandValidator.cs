using FluentValidation;

namespace Stockly.Application.Features.Admin.SetQuotaOverride;

public sealed class SetQuotaOverrideCommandValidator : AbstractValidator<SetQuotaOverrideCommand>
{
    public SetQuotaOverrideCommandValidator()
    {
        RuleFor(c => c.DailyScanQuota).InclusiveBetween(0, AdminLimits.MaxDailyScanQuota);
        RuleFor(c => c.Reason).Reason();
    }
}
