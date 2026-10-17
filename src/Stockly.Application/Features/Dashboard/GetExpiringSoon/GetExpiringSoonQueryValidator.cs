using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Dashboard.GetExpiringSoon;

/// <summary>D2: <c>days</c> 1..365 and paging (<c>pageSize &gt; 100</c> gives <c>400 Validation.Failed</c>).</summary>
public sealed class GetExpiringSoonQueryValidator : AbstractValidator<GetExpiringSoonQuery>
{
    public GetExpiringSoonQueryValidator()
    {
        RuleFor(q => q.Days).InclusiveBetween(1, DashboardLimits.MaxExpiringDays);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
