using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.SetQuotaOverride;

/// <summary>
/// AD9: sets (<c>0..10000</c>) or removes (<c>null</c>) the daily scan quota override of the shop's active subscription.
/// The scan reads the effective quota from PostgreSQL, so it applies from the next scan. Setting the current value again
/// changes nothing and writes no audit entry.
/// </summary>
public sealed record SetQuotaOverrideCommand(Guid ShopId, int? DailyScanQuota, string? Reason) : ICommand<AdminSubscriptionDto>;
