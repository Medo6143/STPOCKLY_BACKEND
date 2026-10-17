using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.CreatePlan;

/// <summary>
/// AD12: a new plan. The contract has one <see cref="Name"/>; it is stored as both the Arabic and the English name
/// (no schema change), and responses show it in the request's language.
/// </summary>
public sealed record CreatePlanCommand(string Code, string Name, int DailyScanQuota, int MaxMembers, string MonthlyPrice, bool IsActive)
    : ICommand<PlanDto>;
