using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Common;
using Stockly.Contracts.Dashboard;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Dashboard.GetExpiringSoon;

/// <summary>D2. The window ends <c>days</c> after today's Cairo date; lines already past their date are included (still in stock).</summary>
public sealed class GetExpiringSoonQueryHandler(IDashboardQueries queries, IClock clock)
    : IRequestHandler<GetExpiringSoonQuery, Result<PagedResponse<ExpiringItem>>>
{
    public async Task<Result<PagedResponse<ExpiringItem>>> Handle(GetExpiringSoonQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await queries.ListExpiringAsync(clock.Today().AddDays(request.Days), request.Page, request.PageSize, cancellationToken);
    }
}
