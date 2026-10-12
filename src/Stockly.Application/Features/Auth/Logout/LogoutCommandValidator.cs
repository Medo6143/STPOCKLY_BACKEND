using FluentValidation;

namespace Stockly.Application.Features.Auth.Logout;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(AuthLimits.RefreshTokenMaxLength);
    }
}
