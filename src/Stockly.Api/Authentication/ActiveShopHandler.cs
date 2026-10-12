using Microsoft.AspNetCore.Authorization;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;

namespace Stockly.Api.Authentication;

/// <summary>
/// Reads the token's shop status (no tracking, one indexed lookup). A suspended shop fails with a reason that
/// <see cref="ProblemAuthorizationResultHandler"/> turns into <c>403 Shop.Suspended</c>.
/// </summary>
public sealed class ActiveShopHandler(ICurrentTenant currentTenant, IShopRepository shops) : AuthorizationHandler<ActiveShopRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveShopRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated != true || currentTenant.ShopId is not { } shopId)
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var status = await shops.GetStatusAsync(shopId, cancellationToken);
        if (status == ShopStatus.Active)
        {
            context.Succeed(requirement);
        }
        else if (status == ShopStatus.Suspended)
        {
            context.Fail(new AuthorizationFailureReason(this, ShopErrors.Suspended.Code));
        }
    }
}
