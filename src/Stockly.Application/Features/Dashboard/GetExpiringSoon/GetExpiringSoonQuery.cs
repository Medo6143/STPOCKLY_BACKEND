using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Common;
using Stockly.Contracts.Dashboard;

namespace Stockly.Application.Features.Dashboard.GetExpiringSoon;

/// <summary>
/// D2: confirmed invoice lines whose expiry date is within <see cref="Days"/> days of today (Africa/Cairo) and whose
/// product still has stock, soonest first (ADR-0004: a hint).
/// </summary>
public sealed record GetExpiringSoonQuery(int Days, int Page, int PageSize) : IQuery<PagedResponse<ExpiringItem>>;
