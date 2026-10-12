using FluentValidation;

namespace Stockly.Application.Features.Auth.Refresh;

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(AuthLimits.RefreshTokenMaxLength);
    }
}
