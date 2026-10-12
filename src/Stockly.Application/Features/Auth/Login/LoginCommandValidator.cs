using FluentValidation;

namespace Stockly.Application.Features.Auth.Login;

/// <summary>Shape only. The password policy is not checked here: a wrong password is <c>Auth.InvalidCredentials</c>.</summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(AuthLimits.EmailMaxLength);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(AuthLimits.PasswordMaxLength);
        RuleFor(c => c.DeviceId).MaximumLength(AuthLimits.DeviceIdMaxLength);
    }
}
