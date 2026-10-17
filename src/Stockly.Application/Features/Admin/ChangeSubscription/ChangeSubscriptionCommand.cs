using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.ChangeSubscription;

/// <summary>
/// AD8: ends the shop's active subscription today and starts a new one to <see cref="PlanId"/> from today, ending on
/// <see cref="EndsOn"/> (inclusive) or open-ended. The quota override is carried over. <see cref="Reason"/> is optional
/// here (contract §9 lists <c>Admin.ReasonRequired</c> for AD6, AD7, AD9, AD10 and AD19 only) and kept in the audit log.
/// </summary>
public sealed record ChangeSubscriptionCommand(Guid ShopId, Guid PlanId, DateOnly? EndsOn, string? Reason) : ICommand<AdminSubscriptionDto>;
