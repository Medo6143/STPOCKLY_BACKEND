namespace Stockly.Contracts.Dashboard;

/// <summary>
/// D1 <c>GET /api/dashboard/summary</c> (contract §8). "Today" is the Africa/Cairo calendar day; <see cref="GeneratedAt"/>
/// is when the numbers were computed (the response is cached for 2 minutes).
/// </summary>
public sealed record DashboardSummary(
    int ProductCount,
    int LowStockCount,
    int NegativeStockCount,
    int ExpiringSoonCount,
    TodaySales TodaySales,
    int InvoicesAwaitingReview,
    DateTime GeneratedAt);

/// <summary>Today's sales: how many, their total (money string) and how many have a price-overridden line.</summary>
public sealed record TodaySales(int Count, string Total, int OverriddenSalesCount);
