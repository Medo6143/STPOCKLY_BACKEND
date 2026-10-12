using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Auth.GetMe;

/// <summary>
/// User from <c>sub</c>, shop from <c>shop_id</c> (ADR-0002). A token whose user or membership no longer exists is
/// not a session any more: <c>Auth.Unauthenticated</c>, so the client refreshes or signs out.
/// </summary>
public sealed class GetMeQueryHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IUserAccounts users,
    IShopMembershipRepository memberships) : IRequestHandler<GetMeQuery, Result<MeResponse>>
{
    public async Task<Result<MeResponse>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentTenant.ShopId is not { } shopId)
        {
            return AuthErrors.Unauthenticated;
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);
        var membership = user is null ? null : await memberships.FindActiveAsync(userId, shopId, cancellationToken);
        if (user is null || membership is null)
        {
            return AuthErrors.Unauthenticated;
        }

        if (membership.ShopStatus == ShopStatus.Suspended)
        {
            return ShopErrors.Suspended;
        }

        return new MeResponse(user.ToDto(), membership.ToDto());
    }
}
