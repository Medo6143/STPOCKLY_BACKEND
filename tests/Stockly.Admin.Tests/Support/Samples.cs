using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;
using Stockly.Contracts.Shops;

namespace Stockly.Admin.Tests.Support;

/// <summary>Contract-shaped sample data. Costs keep 8 decimals, money 2, exactly as the API sends them.</summary>
public static class Samples
{
    public const string TraceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    public const string Cost = "0.00012345";

    public static readonly Guid ShopId = Guid.Parse("0192f0a0-6c1e-7b3a-9d2e-4f5a6b7c8d9e");
    public static readonly Guid PlanId = Guid.Parse("0192f0a0-6c1e-7b3a-9d2e-000000000001");
    public static readonly Guid InvoiceId = Guid.Parse("0192f0a0-6c1e-7b3a-9d2e-000000000002");

    private static readonly DateTime At = new(2026, 10, 17, 9, 30, 0, DateTimeKind.Utc);

    public static PagedResponse<T> Page<T>(params T[] items) => new(items, 1, 20, items.Length);

    public static AdminShopListItem ShopRow(string name = "صيدلية النور") =>
        new(ShopId, name, ShopType.Pharmacy, ShopStatus.Active, "Cairo", "owner@example.com", "standard", 3, At);

    public static AdminSubscriptionDto Subscription(int? quotaOverride = null) =>
        new(PlanId, "standard", "Standard", new DateOnly(2026, 10, 1), null, 20, quotaOverride, quotaOverride ?? 20);

    public static AdminShopDetail Shop(ShopStatus status = ShopStatus.Active, bool withMembers = true, bool withSubscription = true) =>
        new(ShopId, "صيدلية النور", ShopType.Pharmacy, status, "Cairo", "+201000000000", "Nasr City", At,
            status == ShopStatus.Suspended ? At : null,
            status == ShopStatus.Suspended ? "Unpaid invoice" : null,
            withMembers
                ? [new AdminShopMemberDto(Guid.NewGuid(), "Mona Owner", "owner@example.com", ShopRole.Owner, true, At)]
                : [],
            withSubscription ? Subscription() : null,
            new AdminShopCounts(120, 14, 230),
            3,
            Cost);

    public static PlanDto Plan(bool isActive = true, string code = "standard") =>
        new(Guid.NewGuid(), code, "Standard", 20, 3, "199.50", isActive);

    public static AiUsageDay Day(int offset, int calls = 2, string cost = Cost) =>
        new(new DateOnly(2026, 9, 19).AddDays(offset), calls, 1, 0, 1200, 300, cost);

    public static IReadOnlyList<AiUsageDay> Days(bool withUsage) =>
        Enumerable.Range(0, 30).Select(i => withUsage ? Day(i) : Day(i, 0, "0.00000000") with { CacheHits = 0 }).ToList();

    public static AiUsageByShop UsageByShop() => new(ShopId, "صيدلية النور", 6, 1, Cost);

    public static AiUsageByModel UsageByModel() => new("google/gemini-2.5-flash", 6, 1, Cost, 2400);

    public static AdminFailedInvoice FailedRow() =>
        new(InvoiceId, ShopId, "صيدلية النور", "AI output failed validation", 3, At, At);

    public static InvoiceDetail Invoice(InvoiceStatus status = InvoiceStatus.Failed) =>
        new(InvoiceId, Guid.NewGuid(), status, null, null, null, null, null, null, [],
            status == InvoiceStatus.Failed ? "AI output failed validation" : null, [], At, At, null);

    public static AdminInvoiceDetail InvoiceDetail(string? imageUrl = "http://minio.test/stockly-invoices/a.jpg", string? raw = "{\"items\":[]}") =>
        new(Invoice(), ShopId, "صيدلية النور", imageUrl, raw,
            [new AdminAiUsageDto(Guid.NewGuid(), "google/gemini-2.5-flash", false, false, 1200, 300, Cost, 2400, At)]);

    public static AuditLogEntry Audit() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "Shop.Suspend", "Shop", ShopId, "Unpaid invoice",
            System.Text.Json.JsonDocument.Parse("{\"status\":\"Active\"}").RootElement,
            System.Text.Json.JsonDocument.Parse("{\"status\":\"Suspended\"}").RootElement,
            At);
}
