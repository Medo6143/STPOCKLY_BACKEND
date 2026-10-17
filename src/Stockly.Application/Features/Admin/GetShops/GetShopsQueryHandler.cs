using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;
using Stockly.Domain.Text;
using DomainShopStatus = Stockly.Domain.Shops.ShopStatus;

namespace Stockly.Application.Features.Admin.GetShops;

/// <summary>The search term is normalized like stored names for the name match and upper-cased like Identity for the email prefix.</summary>
public sealed class GetShopsQueryHandler(IAdminShopQueries shops, IClock clock)
    : IRequestHandler<GetShopsQuery, Result<PagedResponse<AdminShopListItem>>>
{
    public async Task<Result<PagedResponse<AdminShopListItem>>> Handle(GetShopsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var term = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var normalized = term is null ? null : ArabicTextNormalizer.Normalize(term);
        return await shops.ListAsync(
            string.IsNullOrEmpty(normalized) ? null : normalized,
            term?.ToUpperInvariant(),
            request.Status is { } status ? (DomainShopStatus)status : null,
            request.Page,
            request.PageSize,
            clock.CairoDayStartUtc(),
            AdminMapping.RequestLanguage(),
            cancellationToken);
    }
}
