using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.ActivateShop;

/// <summary>The members sign in again themselves: their tokens were revoked when the shop was suspended.</summary>
public sealed class ActivateShopCommandHandler(
    ICurrentUser currentUser,
    IAdminShopRepository shops,
    AdminAudit audit,
    IUnitOfWork unitOfWork) : IRequestHandler<ActivateShopCommand, Result>
{
    public async Task<Result> Handle(ActivateShopCommand request, CancellationToken cancellationToken)
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

        var before = new { Status = ShopStatus.Suspended, shop.SuspendedAt, shop.SuspendedReason };
        if (!shop.Activate())
        {
            return Result.Success();
        }

        audit.Record(adminId, AdminActions.ShopActivate, AdminTargets.Shop, shop.Id, reason.Value, before, new { Status = ShopStatus.Active });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
