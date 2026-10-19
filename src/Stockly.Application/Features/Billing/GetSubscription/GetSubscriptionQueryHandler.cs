using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Billing;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Billing.GetSubscription;

/// <summary>
/// The subscription with its plan, the scans since the Cairo day started (the shop's invoices created today, like the
/// admin's <c>scansToday</c>), the active members and the newest pending payment. Every shop has an active subscription
/// from registration on; a shop without one is a data error (500).
/// </summary>
public sealed class GetSubscriptionQueryHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IBillingQueries billing,
    IInvoiceRepository invoices,
    IShopMembershipRepository memberships,
    IClock clock) : IRequestHandler<GetSubscriptionQuery, Result<SubscriptionSummary>>
{
    public async Task<Result<SubscriptionSummary>> Handle(GetSubscriptionQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentTenant.ShopId is not { } shopId)
        {
            return AuthErrors.Unauthenticated;
        }

        var language = AdminMapping.RequestLanguage();
        var subscription = await billing.FindActiveSubscriptionAsync(language, cancellationToken)
            ?? throw new InvalidOperationException("The shop has no active subscription.");
        var scansToday = await invoices.CountCreatedSinceAsync(clock.CairoDayStartUtc(), cancellationToken);
        var members = await memberships.CountActiveMembersAsync(userId, shopId, cancellationToken);
        var pending = await billing.FindNewestPendingPaymentAsync(language, cancellationToken);

        return new SubscriptionSummary(
            subscription.Plan,
            subscription.StartsOn,
            subscription.EndsOn,
            subscription.EffectiveDailyScanQuota,
            scansToday,
            members,
            pending);
    }
}
