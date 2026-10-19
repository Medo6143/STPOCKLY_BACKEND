using FluentValidation;
using Stockly.Application.Validation;
using Stockly.Contracts.Billing;

namespace Stockly.Application.Features.Billing.Checkout;

/// <summary>
/// B3 request shape: a client request id, a plan, a known method, and for <c>Wallet</c> an Egyptian mobile number
/// (<see cref="BillingLimits.WalletPhonePattern"/>). The phone is ignored for the other methods.
/// </summary>
public sealed class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutCommandValidator()
    {
        RuleFor(c => c.ClientRequestId).NotEmpty();
        RuleFor(c => c.PlanId).NotEmpty();
        RuleFor(c => c.Method).IsInEnum();
        RuleFor(c => c.WalletPhone)
            .NotEmpty()
            .Matches(BillingLimits.WalletPhonePattern)
            .WithMessage(_ => ValidationMessages.WalletPhoneInvalid)
            .When(c => c.Method == PaymentMethod.Wallet);
    }
}
