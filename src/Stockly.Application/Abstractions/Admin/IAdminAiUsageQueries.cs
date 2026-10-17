using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// Cross-tenant AI usage aggregates over <c>[fromUtc, toUtc)</c> (AD14–AD16). A call is an OpenRouter call (retries
/// included, cache hits not); a failure is a call that did not succeed. Used only by <c>Application/Features/Admin</c>.
/// </summary>
public interface IAdminAiUsageQueries
{
    /// <summary>One row per Africa/Cairo day that has usage, oldest first (the handler fills the days without usage).</summary>
    Task<IReadOnlyList<AiUsageDay>> DailyAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? shopId, CancellationToken cancellationToken);

    /// <summary>A page of shops with usage, highest cost first.</summary>
    Task<PagedResponse<AiUsageByShop>> ByShopAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Every model with usage, highest cost first.</summary>
    Task<IReadOnlyList<AiUsageByModel>> ByModelAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}
