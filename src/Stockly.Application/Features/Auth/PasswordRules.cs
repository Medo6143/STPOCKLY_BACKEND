using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Auth;

/// <summary>
/// Password policy (contract A1): 8–128 characters, at least one letter and one ASCII digit. Identity enforces the
/// same length and digit rule (<c>RequireDigit</c> counts 0-9 only) as a second line.
/// </summary>
public static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Length(AuthLimits.PasswordMinLength, AuthLimits.PasswordMaxLength)
            .Must(password => password is null || password.Any(char.IsLetter))
            .WithMessage(_ => ValidationMessages.PasswordNeedsLetter)
            .Must(password => password is null || password.Any(char.IsAsciiDigit))
            .WithMessage(_ => ValidationMessages.PasswordNeedsDigit);
}
