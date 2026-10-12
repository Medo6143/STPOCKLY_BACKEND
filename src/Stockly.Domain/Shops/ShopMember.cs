using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

/// <summary>
/// Links a user to a shop with a role (ADR-0002). Like <see cref="Shop"/> it is not an <see cref="ITenantEntity"/>:
/// it maps users to tenants and is read at login, before any tenant exists. Every query on it filters by a user id
/// (ADR-0002, update 2026-10-12). Primary key is (<see cref="ShopId"/>, <see cref="UserId"/>).
/// </summary>
public sealed class ShopMember
{
    private ShopMember(Guid shopId, Guid userId, ShopRole role, bool isActive, DateTimeOffset joinedAt)
    {
        ShopId = shopId;
        UserId = userId;
        Role = role;
        IsActive = isActive;
        JoinedAt = joinedAt;
    }

    public Guid ShopId { get; }

    public Guid UserId { get; }

    public ShopRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset JoinedAt { get; }

    /// <summary>An active membership. A shop has exactly one Owner (enforced by a database index).</summary>
    public static Result<ShopMember> Create(Guid shopId, Guid userId, ShopRole role, DateTimeOffset joinedAt)
    {
        if (shopId == Guid.Empty)
        {
            return ShopMemberErrors.ShopRequired;
        }

        if (userId == Guid.Empty)
        {
            return ShopMemberErrors.UserRequired;
        }

        if (!Enum.IsDefined(role))
        {
            return ShopMemberErrors.UnknownRole;
        }

        return new ShopMember(shopId, userId, role, isActive: true, joinedAt);
    }
}
