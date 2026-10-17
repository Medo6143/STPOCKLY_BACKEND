using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Application.Validation;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.UpdatePlan;

public sealed class UpdatePlanCommandHandler(
    ICurrentUser currentUser,
    IAdminPlanRepository plans,
    AdminAudit audit,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdatePlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(UpdatePlanCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } adminId)
        {
            return AuthErrors.Unauthenticated;
        }

        var price = DecimalInput.Money(request.MonthlyPrice, "monthlyPrice");
        if (price.IsFailure)
        {
            return price.Error;
        }

        var plan = await plans.FindAsync(request.PlanId, cancellationToken);
        if (plan is null)
        {
            return PlanErrors.NotFound;
        }

        var before = plan.AuditState();
        var name = request.Name.Trim();
        var updated = plan.Update(name, name, request.DailyScanQuota, request.MaxMembers, price.Value, request.IsActive);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        audit.Record(adminId, AdminActions.PlanUpdate, AdminTargets.Plan, plan.Id, reason: null, before, plan.AuditState());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return plan.ToDto(AdminMapping.RequestLanguage());
    }
}
