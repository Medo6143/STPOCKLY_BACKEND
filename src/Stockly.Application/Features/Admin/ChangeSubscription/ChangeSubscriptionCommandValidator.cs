using FluentValidation;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.ChangeSubscription;

/// <summary>The new subscription starts today (Africa/Cairo), so it cannot end before today.</summary>
public sealed class ChangeSubscriptionCommandValidator : AbstractValidator<ChangeSubscriptionCommand>
{
    public ChangeSubscriptionCommandValidator(IClock clock)
    {
        RuleFor(c => c.PlanId).NotEmpty();
        RuleFor(c => c.EndsOn)
            .Must(endsOn => endsOn is not { } date || date >= clock.Today())
            .WithMessage(_ => ValidationMessages.EndsBeforeToday);
        RuleFor(c => c.Reason).Reason();
    }
}
