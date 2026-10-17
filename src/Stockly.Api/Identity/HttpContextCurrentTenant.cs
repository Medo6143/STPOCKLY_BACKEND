using Stockly.Application.Abstractions.Identity;
using Stockly.Infrastructure.Jobs;

namespace Stockly.Api.Identity;

/// <summary>
/// The tenant is an open <see cref="RequestTenantScope"/> (registration only), else the shop a background job runs for
/// (<see cref="JobTenantContext"/>, docs/design/backend.md §4.1), otherwise the <c>shop_id</c> claim of the authenticated
/// user (ADR-0002), never a request value. Missing, malformed, empty or ambiguous claims give <c>null</c>, which fails
/// closed in the DbContext.
/// </summary>
public sealed class HttpContextCurrentTenant(IHttpContextAccessor accessor, RequestTenantScope scope, JobTenantContext job) : ICurrentTenant
{
    public Guid? ShopId => scope.ShopId ?? job.ShopId ?? FromClaims();

    private Guid? FromClaims()
    {
        var values = accessor.HttpContext?.User.FindAll(StocklyClaimTypes.ShopId).Select(claim => claim.Value).Distinct().ToList();
        return values is [var single] && Guid.TryParse(single, out var shopId) && shopId != Guid.Empty
            ? shopId
            : null;
    }
}
