using MediatR;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.ResetScanQuota;

/// <summary>
/// AD10: resets the shop's scan counter of today (Africa/Cairo), so its full quota is available again. Not an
/// <c>ICommand</c>: the audit entry is committed first and the Redis counter is reset only after the commit.
/// </summary>
public sealed record ResetScanQuotaCommand(Guid ShopId, string? Reason) : IRequest<Result>;
