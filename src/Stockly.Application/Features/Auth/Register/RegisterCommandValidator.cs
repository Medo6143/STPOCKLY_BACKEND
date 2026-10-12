using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Auth.Register;

/// <summary>
/// Request shape (contract A1). A blank shop name after Arabic normalization and an unknown shop type are not shape
/// errors: they return <c>Shop.NameRequired</c> / <c>Shop.UnknownType</c> from the handler.
/// </summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(AuthLimits.EmailMaxLength).EmailAddress();
        RuleFor(c => c.Password).Password();
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(AuthLimits.FullNameMaxLength);
        RuleFor(c => c.Phone).MaximumLength(AuthLimits.PhoneMaxLength);
        RuleFor(c => c.PreferredLanguage)
            .Must(AuthMapping.IsSupportedLanguage)
            .WithMessage(_ => ValidationMessages.LanguageNotSupported);
        RuleFor(c => c.DeviceId).MaximumLength(AuthLimits.DeviceIdMaxLength);
        RuleFor(c => c.Shop).NotNull();
        When(c => c.Shop is not null, () =>
        {
            RuleFor(c => c.Shop.Name).NotNull().MaximumLength(AuthLimits.ShopNameMaxLength);
            RuleFor(c => c.Shop.ShopType).NotEmpty();
        });
    }
}
