using FluentValidation;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Application.Features.Admin.GetDailyAiUsage;

public sealed class GetDailyAiUsageQueryValidator : AbstractValidator<GetDailyAiUsageQuery>
{
    public GetDailyAiUsageQueryValidator(IClock clock)
    {
        UsageRange.AddRules(this, query => query.From, query => query.To, clock);
    }
}
