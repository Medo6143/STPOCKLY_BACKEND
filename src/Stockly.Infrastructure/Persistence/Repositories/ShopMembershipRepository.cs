using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Repositories;

/// <summary>
/// The only code that touches the <see cref="ShopMember"/> set (ADR-0002, update 2026-10-12; architecture test
/// <c>ShopMemberAccessTests</c>). Every read starts from <see cref="ActiveMembersOf"/>, which filters by user id.
/// </summary>
internal sealed class ShopMembershipRepository(StocklyDbContext db) : IShopMembershipRepository
{
    public async Task<ShopMembership?> FindFirstActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await WithShop(ActiveMembersOf(userId))
            .OrderBy(membership => membership.JoinedAt)
            .ThenBy(membership => membership.ShopId)
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToMembership();
    }

    public async Task<ShopMembership?> FindActiveAsync(Guid userId, Guid shopId, CancellationToken cancellationToken)
    {
        var row = await WithShop(ActiveMembersOf(userId).Where(member => member.ShopId == shopId))
            .SingleOrDefaultAsync(cancellationToken);
        return row?.ToMembership();
    }

    // The caller's own active membership (PK shop_id, user_id) gates the count; the shop's members by the PK prefix.
    public Task<int> CountActiveMembersAsync(Guid userId, Guid shopId, CancellationToken cancellationToken)
    {
        var caller = ActiveMembersOf(userId).Where(member => member.ShopId == shopId);
        return db.Set<ShopMember>().AsNoTracking()
            .CountAsync(member => member.ShopId == shopId && member.IsActive && caller.Any(), cancellationToken);
    }

    public void Add(ShopMember member) => db.Set<ShopMember>().Add(member);

    private IQueryable<ShopMember> ActiveMembersOf(Guid userId) =>
        db.Set<ShopMember>().AsNoTracking().Where(member => member.UserId == userId && member.IsActive);

    private IQueryable<MembershipRow> WithShop(IQueryable<ShopMember> members) =>
        members.Join(
            db.Shops.AsNoTracking(),
            member => member.ShopId,
            shop => shop.Id,
            (member, shop) => new MembershipRow
            {
                UserId = member.UserId,
                ShopId = member.ShopId,
                ShopName = shop.Name,
                ShopType = shop.ShopType,
                ShopStatus = shop.Status,
                Role = member.Role,
                JoinedAt = member.JoinedAt,
            });

    // Member-init projection so EF can still order by its members before materializing.
    private sealed class MembershipRow
    {
        public Guid UserId { get; init; }

        public Guid ShopId { get; init; }

        public required string ShopName { get; init; }

        public ShopType ShopType { get; init; }

        public ShopStatus ShopStatus { get; init; }

        public ShopRole Role { get; init; }

        public DateTimeOffset JoinedAt { get; init; }

        public ShopMembership ToMembership() => new(UserId, ShopId, ShopName, ShopType, ShopStatus, Role, JoinedAt);
    }
}
