using Stockly.Contracts.Shops;

namespace Stockly.Contracts.Admin;

/// <summary>One row of AD4 (contract §11.2). <see cref="ScansToday"/> counts the shop's scans of the Africa/Cairo day.</summary>
public sealed record AdminShopListItem(
    Guid Id,
    string Name,
    ShopType ShopType,
    ShopStatus Status,
    string? Governorate,
    string OwnerEmail,
    string? PlanCode,
    int ScansToday,
    DateTime CreatedAt);

/// <summary>
/// A shop's active subscription (AD5, AD8, AD9). <see cref="EffectiveDailyScanQuota"/> is the override when set, else the
/// plan's quota; the scan applies it. Dates are Africa/Cairo business dates.
/// </summary>
public sealed record AdminSubscriptionDto(
    Guid PlanId,
    string PlanCode,
    string PlanName,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int PlanDailyScanQuota,
    int? DailyScanQuotaOverride,
    int EffectiveDailyScanQuota);

/// <summary>AD5 (contract §11.2). <see cref="AiCostLast30DaysUsd"/> is a USD decimal string with 8 decimals.</summary>
public sealed record AdminShopDetail(
    Guid Id,
    string Name,
    ShopType ShopType,
    ShopStatus Status,
    string? Governorate,
    string? Phone,
    string? Address,
    DateTime CreatedAt,
    DateTime? SuspendedAt,
    string? SuspendedReason,
    IReadOnlyList<AdminShopMemberDto> Members,
    AdminSubscriptionDto? Subscription,
    AdminShopCounts Counts,
    int ScansToday,
    string AiCostLast30DaysUsd);

/// <summary>A member of the shop, owner first.</summary>
public sealed record AdminShopMemberDto(Guid UserId, string FullName, string Email, ShopRole Role, bool IsActive, DateTime JoinedAt);

/// <summary>How many products, invoices and sales the shop has.</summary>
public sealed record AdminShopCounts(int Products, int Invoices, int Sales);

/// <summary>Body of the admin writes that only need a reason (AD6, AD7, AD10, AD19); kept in the audit log.</summary>
public sealed record AdminReasonRequest(string? Reason);

/// <summary>AD8: start a new subscription to <see cref="PlanId"/> from today, ending on <see cref="EndsOn"/> (or open-ended).</summary>
public sealed record ChangeSubscriptionRequest(Guid PlanId, DateOnly? EndsOn, string? Reason);

/// <summary>AD9: <see cref="DailyScanQuota"/> <c>0..10000</c>, or <c>null</c> to remove the override.</summary>
public sealed record QuotaOverrideRequest(int? DailyScanQuota, string? Reason);
