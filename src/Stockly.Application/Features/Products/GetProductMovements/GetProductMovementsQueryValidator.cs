using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Products.GetProductMovements;

public sealed class GetProductMovementsQueryValidator : AbstractValidator<GetProductMovementsQuery>
{
    public GetProductMovementsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
