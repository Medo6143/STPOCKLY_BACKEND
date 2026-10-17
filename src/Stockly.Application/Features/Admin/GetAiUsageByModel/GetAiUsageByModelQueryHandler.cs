using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.GetAiUsageByModel;

public sealed class GetAiUsageByModelQueryHandler(IAdminAiUsageQueries usage, IClock clock)
    : IRequestHandler<GetAiUsageByModelQuery, Result<IReadOnlyList<AiUsageByModel>>>
{
    public async Task<Result<IReadOnlyList<AiUsageByModel>>> Handle(GetAiUsageByModelQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var range = UsageRange.Resolve(request.From, request.To, clock);
        var rows = await usage.ByModelAsync(range.StartUtc, range.EndUtc, cancellationToken);
        return Result.Success(rows);
    }
}
