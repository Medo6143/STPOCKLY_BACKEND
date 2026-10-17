using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Application.Validation;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.CreatePlan;

/// <summary>
/// A taken code is <c>409 Plan.CodeTaken</c>: checked first, and a concurrent create that takes the code after the check
/// loses on <c>ux_plans_code</c> with the same error.
/// </summary>
public sealed class CreatePlanCommandHandler(
    ICurrentUser currentUser,
    IAdminPlanRepository plans,
    AdminAudit audit,
    IUnitOfWork unitOfWork) : IRequestHandler<CreatePlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(CreatePlanCommand request, CancellationToken cancellationToken)
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

        if (await plans.CodeExistsAsync(request.Code, cancellationToken))
        {
            return PlanErrors.CodeTaken;
        }

        var name = request.Name.Trim();
        var created = Plan.Create(request.Code, name, name, request.DailyScanQuota, request.MaxMembers, price.Value, request.IsActive);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var plan = created.Value;
        plans.Add(plan);
        audit.Record(adminId, AdminActions.PlanCreate, AdminTargets.Plan, plan.Id, reason: null, before: null, plan.AuditState());
        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error : plan.ToDto(AdminMapping.RequestLanguage());
    }
}
