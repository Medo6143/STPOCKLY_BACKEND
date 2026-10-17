using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// Cross-tenant shop writes for the platform admin (ADR-0005, update 2026-10-18). Loaded entities are tracked and saved
/// with the unit of work, in the same transaction as the audit entry. Used only by <c>Application/Features/Admin</c>.
/// </summary>
public interface IAdminShopRepository
{
    /// <summary>The shop, tracked; <c>null</c> when it does not exist.</summary>
    Task<Shop?> FindAsync(Guid shopId, CancellationToken cancellationToken);

    /// <summary>The shop's active subscription, tracked; <c>null</c> when it has none.</summary>
    Task<ShopSubscription?> FindActiveSubscriptionAsync(Guid shopId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the pending changes (the ended subscription first, so the one-active-subscription index holds), then inserts
    /// <paramref name="subscription"/> for its shop. Must run inside the command's transaction.
    /// </summary>
    Task AddSubscriptionAsync(ShopSubscription subscription, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every active refresh token of every member of the shop (AD6). Refresh tokens are per user, not per shop,
    /// so a member of several shops signs in again everywhere. Returns how many were revoked.
    /// </summary>
    Task<int> RevokeMemberRefreshTokensAsync(Guid shopId, DateTimeOffset revokedAt, CancellationToken cancellationToken);
}
