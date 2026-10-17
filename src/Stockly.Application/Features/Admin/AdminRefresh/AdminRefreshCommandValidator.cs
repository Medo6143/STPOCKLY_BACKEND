using FluentValidation;
using Stockly.Application.Features.Auth;

namespace Stockly.Application.Features.Admin.AdminRefresh;

public sealed class AdminRefreshCommandValidator : AbstractValidator<AdminRefreshCommand>
{
    public AdminRefreshCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(AuthLimits.RefreshTokenMaxLength);
    }
}
