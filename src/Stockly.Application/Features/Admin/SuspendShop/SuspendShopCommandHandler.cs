using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.SuspendShop;

/// <summary>Status, token revocation and the audit entry commit together (one transaction, <c>TransactionBehavior</c>).</summary>
public sealed class SuspendShopCommandHandler(
    ICurrentUser currentUser,
    IAdminShopRepository shops,
    AdminAudit audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<SuspendShopCommand, Result>
{
    public async Task<Result> Handle(SuspendShopCommand request, CancellationToken cancellationToken)
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

        var now = clock.UtcNow;
        if (!shop.Suspend(reason.Value, now))
        {
            return Result.Success();
        }

        var revoked = await shops.RevokeMemberRefreshTokensAsync(shop.Id, now, cancellationToken);
        audit.Record(
            adminId,
            AdminActions.ShopSuspend,
            AdminTargets.Shop,
            shop.Id,
            reason.Value,
            new { Status = ShopStatus.Active },
            new { Status = ShopStatus.Suspended, RevokedRefreshTokens = revoked });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
