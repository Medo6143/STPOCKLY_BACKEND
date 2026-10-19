using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.GetPayments;

public sealed class GetPaymentsQueryValidator : AbstractValidator<GetPaymentsQuery>
{
    public GetPaymentsQueryValidator()
    {
        RuleFor(q => q.Status).IsInEnum();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
