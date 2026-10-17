using System.Globalization;

namespace Stockly.Application.Abstractions.Caching;

/// <summary>
/// The single source of cache key formats and their time to live (docs/design/redis.md §1). Tenant keys start with
/// <c>stockly:{shopId}:</c> (AGENTS.md rule 22).
/// </summary>
public static class CacheKeys
{
    /// <summary><c>products:list</c> entries live 10 minutes.</summary>
    public static readonly TimeSpan ProductListTimeToLive = TimeSpan.FromMinutes(10);

    /// <summary><c>products:lowstock</c> entries live 5 minutes.</summary>
    public static readonly TimeSpan LowStockTimeToLive = TimeSpan.FromMinutes(5);

    /// <summary><c>dashboard:summary</c> entries live 2 minutes (contract D1).</summary>
    public static readonly TimeSpan DashboardSummaryTimeToLive = TimeSpan.FromMinutes(2);

    /// <summary><c>ai:invoice</c> entries live 7 days (content-addressed, never invalidated).</summary>
    public static readonly TimeSpan AiResultTimeToLive = TimeSpan.FromDays(7);

    /// <summary><c>invoice:{id}:status</c> entries live 10 minutes.</summary>
    public static readonly TimeSpan InvoiceStatusTimeToLive = TimeSpan.FromMinutes(10);

    /// <summary>A per-minute scan counter lives 120 seconds: its minute plus a margin.</summary>
    public static readonly TimeSpan ScanRateTimeToLive = TimeSpan.FromSeconds(120);

    /// <summary>A daily scan counter lives 26 hours: its Cairo day plus a margin for DST and clock skew.</summary>
    public static readonly TimeSpan ScanQuotaTimeToLive = TimeSpan.FromHours(26);

    /// <summary>The catalog version counter. No TTL (tiny, never evicted under <c>volatile-lru</c>).</summary>
    public static string CatalogVersion(Guid shopId) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:ver:catalog");

    /// <summary>A page of <c>GET /api/products</c> (P1 without <c>lowStock</c>).</summary>
    public static string ProductList(Guid shopId, long version, string queryHash) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:products:list:v{version}:{queryHash}");

    /// <summary>A page of <c>GET /api/products?lowStock=true</c> (paged, so the key carries the query hash).</summary>
    public static string LowStock(Guid shopId, long version, string queryHash) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:products:lowstock:v{version}:{queryHash}");

    /// <summary>
    /// The D1 dashboard of one Africa/Cairo day (<c>yyyyMMdd</c>): the day in the key makes a value cached just before
    /// midnight unreachable just after it.
    /// </summary>
    public static string DashboardSummary(Guid shopId, long version, DateOnly cairoDate) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:dashboard:summary:v{version}:{cairoDate:yyyyMMdd}");

    /// <summary>The shop's supplier list; deleted when confirm creates a supplier.</summary>
    public static string SuppliersList(Guid shopId) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:suppliers:list");

    /// <summary>The validated AI extraction of an image (lower-case hex SHA-256), per shop.</summary>
    public static string AiInvoice(Guid shopId, string imageSha256) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:ai:invoice:{imageSha256}");

    /// <summary>The polling snapshot of one invoice.</summary>
    public static string InvoiceStatus(Guid shopId, Guid invoiceId) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:invoice:{invoiceId:D}:status");

    /// <summary>The shop's scan counter for one UTC minute (<c>yyyyMMddHHmm</c>).</summary>
    public static string ScanRate(Guid shopId, DateTimeOffset utcNow) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:rl:scan:{utcNow.UtcDateTime:yyyyMMddHHmm}");

    /// <summary>The shop's scan counter for one Africa/Cairo day (<c>yyyyMMdd</c>).</summary>
    public static string ScanQuota(Guid shopId, DateOnly cairoDate) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:quota:scan:{cairoDate:yyyyMMdd}");
}
