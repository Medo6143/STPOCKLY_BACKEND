using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.GetDailyAiUsage;

/// <summary>PostgreSQL returns the days that have usage; every other day of the range is filled with zeros here.</summary>
public sealed class GetDailyAiUsageQueryHandler(IAdminAiUsageQueries usage, IClock clock)
    : IRequestHandler<GetDailyAiUsageQuery, Result<IReadOnlyList<AiUsageDay>>>
{
    public async Task<Result<IReadOnlyList<AiUsageDay>>> Handle(GetDailyAiUsageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var range = UsageRange.Resolve(request.From, request.To, clock);
        var rows = (await usage.DailyAsync(range.StartUtc, range.EndUtc, request.ShopId, cancellationToken))
            .ToDictionary(row => row.Date);
        var zero = AdminMapping.Usd(0m);

        var days = new List<AiUsageDay>(range.To.DayNumber - range.From.DayNumber + 1);
        for (var day = range.From; day <= range.To; day = day.AddDays(1))
        {
            days.Add(rows.TryGetValue(day, out var row) ? row : new AiUsageDay(day, 0, 0, 0, 0, 0, zero));
        }

        return days;
    }
}
