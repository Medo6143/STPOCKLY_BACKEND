using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.GetPlans;

/// <summary>AD11: every plan, active first, then by code (a small table, not paged).</summary>
public sealed record GetPlansQuery : IQuery<IReadOnlyList<PlanDto>>;
