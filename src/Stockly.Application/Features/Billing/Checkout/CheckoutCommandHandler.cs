using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using ContractPaymentMethod = Stockly.Contracts.Billing.PaymentMethod;

namespace Stockly.Application.Features.Billing.Checkout;

/// <summary>
/// B3 (contract §12.2, ADR-0015 §3, §6, §7): return an existing payment with this client request id; check the plan
/// (exists, active, not free), the method (configured) and that no pending payment is open; then ask the provider for
/// the checkout <b>outside</b> any transaction, with the plan's price as the amount. A provider failure returns
/// <c>Billing.ProviderUnavailable</c> and stores nothing. Then, in one short transaction: lock the shop's checkouts,
/// repeat the client-request and pending checks (a concurrent request may have committed meanwhile), insert the payment
/// and read it back. A concurrent repeat that loses the unique insert is answered with the winner's payment.
/// </summary>
public sealed class CheckoutCommandHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IUserAccounts users,
    IPlanRepository plans,
    IPaymentRepository payments,
    IBillingQueries billing,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<CheckoutCommand, Result<CheckoutResult>>
{
    public async Task<Result<CheckoutResult>> Handle(CheckoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId || currentTenant.ShopId is not { } shopId)
        {
            return AuthErrors.Unauthenticated;
        }

        var language = AdminMapping.RequestLanguage();
        var existing = await billing.FindPaymentByClientRequestIdAsync(request.ClientRequestId, language, cancellationToken);
        if (existing is not null)
        {
            return new CheckoutResult(existing, Created: false);
        }

        var plan = await plans.FindAsync(request.PlanId, cancellationToken);
        var method = ToDomain(request.Method);
        var refused = Refuse(plan, method) ?? (await payments.HasOpenPaymentAsync(clock.UtcNow, cancellationToken)
            ? PaymentErrors.PaymentPending
            : null);
        if (refused is not null)
        {
            return refused;
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.Unauthenticated;
        }

        var started = Payment.Start(shopId, plan!.Id, request.ClientRequestId, plan.MonthlyPrice, method, gateway.Provider, clock.UtcNow);
        if (started.IsFailure)
        {
            return started.Error;
        }

        var payment = started.Value;
        var checkout = await gateway.CreateCheckoutAsync(
            new ProviderCheckoutRequest(
                payment.Id,
                payment.Amount,
                payment.Currency,
                method,
                AdminMapping.PlanName(plan.NameAr, plan.NameEn, language),
                new Payer(user.FullName, user.Email, user.Phone),
                method == PaymentMethod.Wallet ? request.WalletPhone : null),
            cancellationToken);
        if (checkout.IsFailure)
        {
            return checkout.Error;
        }

        var attached = payment.AttachCheckout(
            checkout.Value.ProviderOrderId, checkout.Value.CheckoutUrl, checkout.Value.ReferenceNumber, checkout.Value.ExpiresAt);
        if (attached.IsFailure)
        {
            return attached.Error;
        }

        var stored = await unitOfWork.ExecuteInTransactionAsync(
            token => StoreAsync(payment, language, token),
            result => result.IsSuccess && result.Value.Created,
            cancellationToken);
        if (stored.IsSuccess || stored.Error != PaymentErrors.DuplicateClientRequest)
        {
            return stored;
        }

        // A concurrent first request with the same client request id committed first: return its payment.
        var winner = await billing.FindPaymentByClientRequestIdAsync(request.ClientRequestId, language, cancellationToken)
            ?? throw new InvalidOperationException("The payment that won the client request id was not found.");
        return new CheckoutResult(winner, Created: false);
    }

    private async Task<Result<CheckoutResult>> StoreAsync(Payment payment, PreferredLanguage language, CancellationToken cancellationToken)
    {
        await payments.LockCheckoutsAsync(cancellationToken);

        var existing = await billing.FindPaymentByClientRequestIdAsync(payment.ClientRequestId, language, cancellationToken);
        if (existing is not null)
        {
            return new CheckoutResult(existing, Created: false);
        }

        if (await payments.HasOpenPaymentAsync(clock.UtcNow, cancellationToken))
        {
            return PaymentErrors.PaymentPending;
        }

        payments.Add(payment);
        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        var dto = await billing.FindPaymentAsync(payment.Id, language, cancellationToken)
            ?? throw new InvalidOperationException("The payment saved in this transaction was not found.");
        return new CheckoutResult(dto, Created: true);
    }

    // Contract order: the plan first (404, 400 inactive, 400 free), then the method.
    private Error? Refuse(Plan? plan, PaymentMethod method) =>
        plan is null ? PlanErrors.NotFound
        : !plan.IsActive ? PlanErrors.Inactive
        : plan.MonthlyPrice <= 0m ? PaymentErrors.FreePlan
        : !gateway.IsAvailable(method) ? PaymentErrors.MethodUnavailable
        : null;

    private static PaymentMethod ToDomain(ContractPaymentMethod method) => (PaymentMethod)method;
}
