using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Sales.GetSales;

/// <summary>S2 paging (<c>pageSize &gt; 100</c> gives <c>400 Validation.Failed</c>) and a range that does not end before it starts.</summary>
public sealed class GetSalesQueryValidator : AbstractValidator<GetSalesQuery>
{
    public GetSalesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .When(q => q.From is not null && q.To is not null);
    }
}
