using FluentValidation;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.GetAiUsageByShop;

public sealed class GetAiUsageByShopQueryValidator : AbstractValidator<GetAiUsageByShopQuery>
{
    public GetAiUsageByShopQueryValidator(IClock clock)
    {
        UsageRange.AddRules(this, query => query.From, query => query.To, clock);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
