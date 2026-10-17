using FluentValidation;
using Stockly.Application.Features.Auth;

namespace Stockly.Application.Features.Admin.AdminLogin;

/// <summary>Shape only, like A2: a wrong password is <c>Auth.InvalidCredentials</c>, not a validation error.</summary>
public sealed class AdminLoginCommandValidator : AbstractValidator<AdminLoginCommand>
{
    public AdminLoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(AuthLimits.EmailMaxLength);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(AuthLimits.PasswordMaxLength);
    }
}
