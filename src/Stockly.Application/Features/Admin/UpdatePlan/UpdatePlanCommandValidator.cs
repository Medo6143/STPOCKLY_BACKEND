using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.UpdatePlan;

/// <summary>Request shape (contract AD13). Money precision is checked by the handler (<c>Precision.*</c>).</summary>
public sealed class UpdatePlanCommandValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(AdminLimits.PlanNameMaxLength);
        RuleFor(c => c.DailyScanQuota).InclusiveBetween(0, AdminLimits.MaxDailyScanQuota);
        RuleFor(c => c.MaxMembers).InclusiveBetween(1, AdminLimits.MaxPlanMembers);
        RuleFor(c => c.MonthlyPrice).NotNull().DecimalString();
    }
}
