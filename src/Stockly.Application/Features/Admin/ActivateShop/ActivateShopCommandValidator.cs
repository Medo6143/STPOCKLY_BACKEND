using FluentValidation;

namespace Stockly.Application.Features.Admin.ActivateShop;

public sealed class ActivateShopCommandValidator : AbstractValidator<ActivateShopCommand>
{
    public ActivateShopCommandValidator()
    {
        RuleFor(c => c.Reason).Reason();
    }
}
