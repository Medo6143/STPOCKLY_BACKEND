using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.GetShops;

public sealed class GetShopsQueryValidator : AbstractValidator<GetShopsQuery>
{
    public GetShopsQueryValidator()
    {
        RuleFor(q => q.Search).MaximumLength(AdminLimits.SearchMaxLength);
        RuleFor(q => q.Status).IsInEnum();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
