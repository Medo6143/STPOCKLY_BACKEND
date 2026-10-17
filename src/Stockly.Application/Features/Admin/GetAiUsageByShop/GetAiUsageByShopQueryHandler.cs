using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.GetAiUsageByShop;

public sealed class GetAiUsageByShopQueryHandler(IAdminAiUsageQueries usage, IClock clock)
    : IRequestHandler<GetAiUsageByShopQuery, Result<PagedResponse<AiUsageByShop>>>
{
    public async Task<Result<PagedResponse<AiUsageByShop>>> Handle(GetAiUsageByShopQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var range = UsageRange.Resolve(request.From, request.To, clock);
        return await usage.ByShopAsync(range.StartUtc, range.EndUtc, request.Page, request.PageSize, cancellationToken);
    }
}
