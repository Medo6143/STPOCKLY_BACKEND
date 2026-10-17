using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.ChangeSubscription;

/// <summary>
/// Shop → plan → end the current subscription → start the new one → audit, in one transaction. The response is read back
/// from PostgreSQL inside the transaction.
/// </summary>
public sealed class ChangeSubscriptionCommandHandler(
    ICurrentUser currentUser,
    IAdminShopRepository shops,
    IAdminShopQueries shopQueries,
    IAdminPlanRepository plans,
    AdminAudit audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<ChangeSubscriptionCommand, Result<AdminSubscriptionDto>>
{
    public async Task<Result<AdminSubscriptionDto>> Handle(ChangeSubscriptionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } adminId)
        {
            return AuthErrors.Unauthenticated;
        }

        var shop = await shops.FindAsync(request.ShopId, cancellationToken);
        if (shop is null)
        {
            return ShopErrors.NotFound;
        }

        var plan = await plans.FindAsync(request.PlanId, cancellationToken);
        if (plan is null)
        {
            return PlanErrors.NotFound;
        }

        if (!plan.IsActive)
        {
            return PlanErrors.Inactive;
        }

        var today = clock.Today();
        var current = await shops.FindActiveSubscriptionAsync(shop.Id, cancellationToken);
        var before = current is null
            ? null
            : new { current.PlanId, current.StartsOn, current.EndsOn, current.DailyScanQuotaOverride };
        if (current is not null)
        {
            var ended = current.End(today);
            if (ended.IsFailure)
            {
                return ended.Error;
            }
        }

        var started = ShopSubscription.Start(shop.Id, plan.Id, today, request.EndsOn, current?.DailyScanQuotaOverride, clock.UtcNow);
        if (started.IsFailure)
        {
            return started.Error;
        }

        var next = started.Value;
        await shops.AddSubscriptionAsync(next, cancellationToken);
        audit.Record(
            adminId,
            AdminActions.ShopChangeSubscription,
            AdminTargets.Shop,
            shop.Id,
            request.Reason,
            before,
            new { next.PlanId, PlanCode = plan.Code, next.StartsOn, next.EndsOn, next.DailyScanQuotaOverride });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await shopQueries.FindSubscriptionAsync(shop.Id, AdminMapping.RequestLanguage(), cancellationToken)
            ?? throw new InvalidOperationException("The subscription just started was not found.");
    }
}
