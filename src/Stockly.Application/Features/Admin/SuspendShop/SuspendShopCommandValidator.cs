using FluentValidation;

namespace Stockly.Application.Features.Admin.SuspendShop;

public sealed class SuspendShopCommandValidator : AbstractValidator<SuspendShopCommand>
{
    public SuspendShopCommandValidator()
    {
        RuleFor(c => c.Reason).Reason();
    }
}
