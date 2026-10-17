using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.SetQuotaOverride;

/// <summary>
/// Every shop has an active subscription from registration on, and AD8 always replaces it with another: a shop without
/// one is a data error (500), not a client error.
/// </summary>
public sealed class SetQuotaOverrideCommandHandler(
    ICurrentUser currentUser,
    IAdminShopRepository shops,
    IAdminShopQueries shopQueries,
    AdminAudit audit,
    IUnitOfWork unitOfWork) : IRequestHandler<SetQuotaOverrideCommand, Result<AdminSubscriptionDto>>
{
    public async Task<Result<AdminSubscriptionDto>> Handle(SetQuotaOverrideCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } adminId)
        {
            return AuthErrors.Unauthenticated;
        }

        var reason = AdminReason.Require(request.Reason);
        if (reason.IsFailure)
        {
            return reason.Error;
        }

        var shop = await shops.FindAsync(request.ShopId, cancellationToken);
        if (shop is null)
        {
            return ShopErrors.NotFound;
        }

        var subscription = await shops.FindActiveSubscriptionAsync(shop.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Shop {shop.Id} has no active subscription.");
        var previous = subscription.DailyScanQuotaOverride;
        var changed = subscription.SetQuotaOverride(request.DailyScanQuota);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (changed.Value)
        {
            audit.Record(
                adminId,
                AdminActions.ShopSetQuotaOverride,
                AdminTargets.Shop,
                shop.Id,
                reason.Value,
                new { DailyScanQuotaOverride = previous },
                new { subscription.DailyScanQuotaOverride });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await shopQueries.FindSubscriptionAsync(shop.Id, AdminMapping.RequestLanguage(), cancellationToken)
            ?? throw new InvalidOperationException($"Shop {shop.Id} has no active subscription.");
    }
}
