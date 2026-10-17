using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.GetShop;

public sealed class GetShopQueryHandler(IAdminShopQueries shops, IClock clock) : IRequestHandler<GetShopQuery, Result<AdminShopDetail>>
{
    public async Task<Result<AdminShopDetail>> Handle(GetShopQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var detail = await shops.FindDetailAsync(
            request.ShopId, clock.CairoDayStartUtc(), clock.UtcNow - AdminLimits.AiCostWindow, AdminMapping.RequestLanguage(), cancellationToken);
        return detail is null ? ShopErrors.NotFound : detail;
    }
}
