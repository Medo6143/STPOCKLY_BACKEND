using FluentValidation;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Application.Features.Admin.GetPaymentSummary;

public sealed class GetPaymentSummaryQueryValidator : AbstractValidator<GetPaymentSummaryQuery>
{
    public GetPaymentSummaryQueryValidator(IClock clock)
    {
        UsageRange.AddRules(this, query => query.From, query => query.To, clock);
    }
}
