using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.GetFailedInvoices;

public sealed class GetFailedInvoicesQueryValidator : AbstractValidator<GetFailedInvoicesQuery>
{
    public GetFailedInvoicesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
