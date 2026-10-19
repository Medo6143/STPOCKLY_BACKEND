using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Billing.GetPayments;

/// <summary>B5 paging: <c>pageSize &gt; 100</c> is <c>400 Validation.Failed</c> (rule 30).</summary>
public sealed class GetPaymentsQueryValidator : AbstractValidator<GetPaymentsQuery>
{
    public GetPaymentsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
