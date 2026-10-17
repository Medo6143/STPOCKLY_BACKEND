using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.GetPlans;

public sealed class GetPlansQueryHandler(IAdminPlanRepository plans) : IRequestHandler<GetPlansQuery, Result<IReadOnlyList<PlanDto>>>
{
    public async Task<Result<IReadOnlyList<PlanDto>>> Handle(GetPlansQuery request, CancellationToken cancellationToken)
    {
        var language = AdminMapping.RequestLanguage();
        var all = await plans.ListAsync(cancellationToken);
        return all.Select(plan => plan.ToDto(language)).ToList();
    }
}
