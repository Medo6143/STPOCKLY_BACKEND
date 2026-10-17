using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Domain.Sales;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using ContractShopRole = Stockly.Contracts.Shops.ShopRole;
using ContractShopStatus = Stockly.Contracts.Shops.ShopStatus;
using ContractShopType = Stockly.Contracts.Shops.ShopType;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>
/// Cross-tenant shop reads of the platform admin (ADR-0005 and ADR-0002, updates 2026-10-18): the only place besides
/// <c>ShopMembershipRepository</c> that reads <c>shop_members</c>, here by shop id. Projections, no tracking. Indexes
/// in docs/erd.md §3.1 and §3.6.
/// </summary>
internal sealed class AdminShopQueries(StocklyDbContext db) : IAdminShopQueries
{
    private const string LikeEscape = "\\";

    // Status filter: ix_shops_status_created_at; none: ix_shops_created_at. Name search: ix_shops_normalized_name_trgm
    // (ILIKE '%term%'); owner email prefix: ix_users_normalized_email_pattern (LIKE 'TERM%') through shop_members' PK.
    // Per row of the page: the owner (shop_members PK), the plan code (ux_shop_subscriptions_shop_id_active) and today's
    // scans (ix_invoices_shop_id_created_at), as correlated subqueries of one statement.
    public async Task<PagedResponse<AdminShopListItem>> ListAsync(
        string? normalizedSearch,
        string? emailPrefix,
        ShopStatus? status,
        int page,
        int pageSize,
        DateTimeOffset cairoDayStart,
        PreferredLanguage language,
        CancellationToken cancellationToken)
    {
        var members = db.Set<ShopMember>().AsNoTracking();
        var users = db.Users.AsNoTracking();
        var shops = db.Shops.AsNoTracking();
        if (status is { } wanted)
        {
            shops = shops.Where(shop => shop.Status == wanted);
        }

        if (normalizedSearch is not null || emailPrefix is not null)
        {
            var contains = normalizedSearch is null ? null : "%" + EscapeLike(normalizedSearch) + "%";
            var prefix = emailPrefix is null ? null : EscapeLike(emailPrefix) + "%";
            shops = shops.Where(shop =>
                (contains != null && EF.Functions.ILike(shop.NormalizedName, contains, LikeEscape))
                || (prefix != null && members.Any(member =>
                    member.ShopId == shop.Id
                    && member.Role == ShopRole.Owner
                    && users.Any(user => user.Id == member.UserId && EF.Functions.Like(user.NormalizedEmail!, prefix, LikeEscape)))));
        }

        var totalCount = await shops.CountAsync(cancellationToken);
        var rows = await shops
            .OrderByDescending(shop => shop.CreatedAt)
            .ThenByDescending(shop => shop.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(shop => new
            {
                shop.Id,
                shop.Name,
                shop.ShopType,
                shop.Status,
                shop.Governorate,
                shop.CreatedAt,
                OwnerEmail = members
                    .Where(member => member.ShopId == shop.Id && member.Role == ShopRole.Owner)
                    .Join(users, member => member.UserId, user => user.Id, (_, user) => user.Email)
                    .FirstOrDefault(),
                PlanCode = db.Set<ShopSubscription>().IgnoreQueryFilters()
                    .Where(subscription => subscription.ShopId == shop.Id && subscription.Status == SubscriptionStatus.Active)
                    .Join(db.Set<Plan>(), subscription => subscription.PlanId, plan => plan.Id, (_, plan) => plan.Code)
                    .FirstOrDefault(),
                ScansToday = db.Set<Invoice>().IgnoreQueryFilters()
                    .Count(invoice => invoice.ShopId == shop.Id && invoice.CreatedAt >= cairoDayStart),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AdminShopListItem(
                row.Id,
                row.Name,
                (ContractShopType)row.ShopType,
                (ContractShopStatus)row.Status,
                row.Governorate,
                row.OwnerEmail ?? string.Empty,
                row.PlanCode,
                row.ScansToday,
                row.CreatedAt.UtcDateTime))
            .ToList();
        return new PagedResponse<AdminShopListItem>(items, page, pageSize, totalCount);
    }

    // One statement for the shop and its counts (each by its (shop_id, ...) index), one for the members (shop_members PK),
    // one for the subscription.
    public async Task<AdminShopDetail?> FindDetailAsync(
        Guid shopId, DateTimeOffset cairoDayStart, DateTimeOffset costSince, PreferredLanguage language, CancellationToken cancellationToken)
    {
        var shop = await db.Shops.AsNoTracking()
            .Where(candidate => candidate.Id == shopId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.ShopType,
                candidate.Status,
                candidate.Governorate,
                candidate.Phone,
                candidate.Address,
                candidate.CreatedAt,
                candidate.SuspendedAt,
                candidate.SuspendedReason,
                Products = db.Products.IgnoreQueryFilters().Count(product => product.ShopId == candidate.Id),
                Invoices = db.Set<Invoice>().IgnoreQueryFilters().Count(invoice => invoice.ShopId == candidate.Id),
                Sales = db.Set<Sale>().IgnoreQueryFilters().Count(sale => sale.ShopId == candidate.Id),
                ScansToday = db.Set<Invoice>().IgnoreQueryFilters()
                    .Count(invoice => invoice.ShopId == candidate.Id && invoice.CreatedAt >= cairoDayStart),
                AiCost = db.Set<AiUsageRecord>().IgnoreQueryFilters()
                    .Where(record => record.ShopId == candidate.Id && record.CreatedAt >= costSince)
                    .Sum(record => (decimal?)record.CostUsd) ?? 0m,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (shop is null)
        {
            return null;
        }

        var members = await db.Set<ShopMember>().AsNoTracking()
            .Where(member => member.ShopId == shopId)
            .Join(db.Users.AsNoTracking(), member => member.UserId, user => user.Id, (member, user) => new
            {
                member.UserId,
                user.FullName,
                user.Email,
                member.Role,
                member.IsActive,
                member.JoinedAt,
            })
            .OrderBy(member => member.Role)
            .ThenBy(member => member.JoinedAt)
            .ToListAsync(cancellationToken);

        var subscription = await FindSubscriptionAsync(shopId, language, cancellationToken);
        return new AdminShopDetail(
            shop.Id,
            shop.Name,
            (ContractShopType)shop.ShopType,
            (ContractShopStatus)shop.Status,
            shop.Governorate,
            shop.Phone,
            shop.Address,
            shop.CreatedAt.UtcDateTime,
            shop.SuspendedAt?.UtcDateTime,
            shop.SuspendedReason,
            [.. members.Select(member => new AdminShopMemberDto(
                member.UserId,
                member.FullName,
                member.Email ?? string.Empty,
                (ContractShopRole)member.Role,
                member.IsActive,
                member.JoinedAt.UtcDateTime))],
            subscription,
            new AdminShopCounts(shop.Products, shop.Invoices, shop.Sales),
            shop.ScansToday,
            AdminMapping.Usd(shop.AiCost));
    }

    // ux_shop_subscriptions_shop_id_active, then the plan by primary key.
    public async Task<AdminSubscriptionDto?> FindSubscriptionAsync(Guid shopId, PreferredLanguage language, CancellationToken cancellationToken)
    {
        var row = await db.Set<ShopSubscription>().IgnoreQueryFilters().AsNoTracking()
            .Where(subscription => subscription.ShopId == shopId && subscription.Status == SubscriptionStatus.Active)
            .Join(db.Set<Plan>().AsNoTracking(), subscription => subscription.PlanId, plan => plan.Id, (subscription, plan) => new
            {
                subscription.PlanId,
                plan.Code,
                plan.NameAr,
                plan.NameEn,
                subscription.StartsOn,
                subscription.EndsOn,
                plan.DailyScanQuota,
                subscription.DailyScanQuotaOverride,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new AdminSubscriptionDto(
                row.PlanId,
                row.Code,
                AdminMapping.PlanName(row.NameAr, row.NameEn, language),
                row.StartsOn,
                row.EndsOn,
                row.DailyScanQuota,
                row.DailyScanQuotaOverride,
                row.DailyScanQuotaOverride ?? row.DailyScanQuota);
    }

    private static string EscapeLike(string term) =>
        term.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
