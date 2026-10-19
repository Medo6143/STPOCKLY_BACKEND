using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>
/// The only access path to shop memberships (ADR-0002, update 2026-10-12). <see cref="ShopMember"/> is not a tenant
/// entity, so every read takes the user id it is filtered by: a user only ever sees their own memberships.
/// </summary>
public interface IShopMembershipRepository
{
    /// <summary>The user's active membership joined first (contract decision #6), with its shop; <c>null</c> when none.</summary>
    Task<ShopMembership?> FindFirstActiveAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The user's active membership in <paramref name="shopId"/>, with the shop; <c>null</c> when none.</summary>
    Task<ShopMembership?> FindActiveAsync(Guid userId, Guid shopId, CancellationToken cancellationToken);

    /// <summary>
    /// The active members of <paramref name="shopId"/> (B2 <c>memberCount</c>), counted only when
    /// <paramref name="userId"/> is one of them; otherwise 0.
    /// </summary>
    Task<int> CountActiveMembersAsync(Guid userId, Guid shopId, CancellationToken cancellationToken);

    /// <summary>Adds a membership. The member's own <see cref="ShopMember.UserId"/> is the user it belongs to.</summary>
    void Add(ShopMember member);
}

/// <summary>A user's membership joined with its shop: everything a token and <c>ShopMembershipDto</c> need.</summary>
public sealed record ShopMembership(
    Guid UserId, Guid ShopId, string ShopName, ShopType ShopType, ShopStatus ShopStatus, ShopRole Role, DateTimeOffset JoinedAt);
