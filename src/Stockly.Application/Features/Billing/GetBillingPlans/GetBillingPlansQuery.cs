using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Billing;

namespace Stockly.Application.Features.Billing.GetBillingPlans;

/// <summary>B1: the active plans, free first, then by price; the shop's current plan marked.</summary>
public sealed record GetBillingPlansQuery : IQuery<IReadOnlyList<BillingPlan>>;
