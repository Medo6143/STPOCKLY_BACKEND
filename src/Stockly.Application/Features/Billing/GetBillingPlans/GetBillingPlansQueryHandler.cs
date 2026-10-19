using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Billing;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Billing.GetBillingPlans;

public sealed class GetBillingPlansQueryHandler(IBillingQueries billing)
    : IRequestHandler<GetBillingPlansQuery, Result<IReadOnlyList<BillingPlan>>>
{
    public async Task<Result<IReadOnlyList<BillingPlan>>> Handle(GetBillingPlansQuery request, CancellationToken cancellationToken) =>
        Result.Success(await billing.ListActivePlansAsync(AdminMapping.RequestLanguage(), cancellationToken));
}
