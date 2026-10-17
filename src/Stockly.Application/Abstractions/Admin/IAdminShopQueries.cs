using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// Cross-tenant shop reads for the platform admin (ADR-0005, update 2026-10-18): projections, no tracking. Implemented in
/// <c>Infrastructure/Persistence/Admin</c>; used only by <c>Application/Features/Admin</c>. Plan names are in the given
/// language (contract: the request's <c>Accept-Language</c>).
/// </summary>
public interface IAdminShopQueries
{
    /// <summary>
    /// AD4: a page of shops, newest first. <paramref name="normalizedSearch"/> (Arabic-normalized) is contained in the
    /// shop's normalized name, or <paramref name="emailPrefix"/> (Identity-normalized, upper case) starts the owner's
    /// email. <c>scansToday</c> counts the shop's invoices created since <paramref name="cairoDayStart"/>.
    /// </summary>
    Task<PagedResponse<AdminShopListItem>> ListAsync(
        string? normalizedSearch,
        string? emailPrefix,
        ShopStatus? status,
        int page,
        int pageSize,
        DateTimeOffset cairoDayStart,
        PreferredLanguage language,
        CancellationToken cancellationToken);

    /// <summary>AD5: the shop with members, subscription and counts; <c>null</c> when it does not exist.</summary>
    Task<AdminShopDetail?> FindDetailAsync(
        Guid shopId, DateTimeOffset cairoDayStart, DateTimeOffset costSince, PreferredLanguage language, CancellationToken cancellationToken);

    /// <summary>The shop's active subscription with its plan (AD8, AD9 responses); <c>null</c> when it has none.</summary>
    Task<AdminSubscriptionDto?> FindSubscriptionAsync(Guid shopId, PreferredLanguage language, CancellationToken cancellationToken);
}
