using FluentValidation;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Application.Features.Admin.GetAiUsageByModel;

public sealed class GetAiUsageByModelQueryValidator : AbstractValidator<GetAiUsageByModelQuery>
{
    public GetAiUsageByModelQueryValidator(IClock clock)
    {
        UsageRange.AddRules(this, query => query.From, query => query.To, clock);
    }
}
