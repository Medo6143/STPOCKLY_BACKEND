using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.CreatePlan;

/// <summary>Request shape (contract AD12). Money precision is checked by the handler (<c>Precision.*</c>).</summary>
public sealed class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().Matches(AdminLimits.PlanCodePattern);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(AdminLimits.PlanNameMaxLength);
        RuleFor(c => c.DailyScanQuota).InclusiveBetween(0, AdminLimits.MaxDailyScanQuota);
        RuleFor(c => c.MaxMembers).InclusiveBetween(1, AdminLimits.MaxPlanMembers);
        RuleFor(c => c.MonthlyPrice).NotNull().DecimalString();
    }
}
