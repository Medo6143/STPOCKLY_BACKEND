using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.ResetScanQuota;

/// <summary>
/// One save holds the audit entry (one statement, its own transaction); the counter is deleted after it. With Redis down
/// the counter cannot be reset (the scan then counts today's invoices in PostgreSQL) and the limiter logs the warning;
/// the request still succeeds (rule 25).
/// </summary>
public sealed class ResetScanQuotaCommandHandler(
    ICurrentUser currentUser,
    IAdminShopRepository shops,
    IScanQuotaCounter quota,
    AdminAudit audit,
    IUnitOfWork unitOfWork) : IRequestHandler<ResetScanQuotaCommand, Result>
{
    public async Task<Result> Handle(ResetScanQuotaCommand request, CancellationToken cancellationToken)
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

        audit.Record(adminId, AdminActions.ShopResetQuota, AdminTargets.Shop, shop.Id, reason.Value, before: null, after: null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await quota.ResetAsync(shop.Id, cancellationToken);
        return Result.Success();
    }
}
