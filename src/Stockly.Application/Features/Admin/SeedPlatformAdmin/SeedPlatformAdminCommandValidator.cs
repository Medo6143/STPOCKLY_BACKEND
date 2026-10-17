using FluentValidation;
using Stockly.Application.Features.Auth;

namespace Stockly.Application.Features.Admin.SeedPlatformAdmin;

/// <summary>The configured admin follows the same email and password policy as a registered user (contract A1).</summary>
public sealed class SeedPlatformAdminCommandValidator : AbstractValidator<SeedPlatformAdminCommand>
{
    public SeedPlatformAdminCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(AuthLimits.EmailMaxLength).EmailAddress();
        RuleFor(c => c.Password).Password();
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(AuthLimits.FullNameMaxLength);
    }
}
