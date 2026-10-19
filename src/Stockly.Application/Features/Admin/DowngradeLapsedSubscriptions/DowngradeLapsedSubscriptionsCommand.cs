using MediatR;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.DowngradeLapsedSubscriptions;

/// <summary>
/// The daily downgrade (ADR-0015 §5): a shop whose subscription to another plan than the default ended before today is
/// moved to the default free plan, open-ended, from today; the quota override is kept. A system job: no admin user, no
/// audit entry. Each shop gets its own short transaction. Returns the number of shops moved.
/// </summary>
public sealed record DowngradeLapsedSubscriptionsCommand : IRequest<Result<int>>;
