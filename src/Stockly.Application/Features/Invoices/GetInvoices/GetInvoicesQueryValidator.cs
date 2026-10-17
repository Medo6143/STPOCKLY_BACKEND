using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Invoices.GetInvoices;

/// <summary>I2 request shape: a known status and paging (<c>pageSize &gt; 100</c> gives <c>400 Validation.Failed</c>, rule 30).</summary>
public sealed class GetInvoicesQueryValidator : AbstractValidator<GetInvoicesQuery>
{
    public GetInvoicesQueryValidator()
    {
        RuleFor(q => q.Status).IsInEnum();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
