using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.UpdatePlan;

/// <summary>
/// AD13: changes every field but the code (immutable). <see cref="Name"/> replaces both the Arabic and the English name.
/// Shops on the plan see a new quota from their next scan.
/// </summary>
public sealed record UpdatePlanCommand(Guid PlanId, string Name, int DailyScanQuota, int MaxMembers, string MonthlyPrice, bool IsActive)
    : ICommand<PlanDto>;
