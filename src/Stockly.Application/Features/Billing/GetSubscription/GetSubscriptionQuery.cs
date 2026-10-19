using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Billing;

namespace Stockly.Application.Features.Billing.GetSubscription;

/// <summary>B2: the shop's active subscription, its usage today, its members and its newest pending payment.</summary>
public sealed record GetSubscriptionQuery : IQuery<SubscriptionSummary>;
