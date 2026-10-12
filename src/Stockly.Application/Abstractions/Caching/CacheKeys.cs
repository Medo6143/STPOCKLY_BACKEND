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

    /// <summary>The catalog version counter. No TTL (tiny, never evicted under <c>volatile-lru</c>).</summary>
    public static string CatalogVersion(Guid shopId) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:ver:catalog");

    /// <summary>A page of <c>GET /api/products</c> (P1 without <c>lowStock</c>).</summary>
    public static string ProductList(Guid shopId, long version, string queryHash) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:products:list:v{version}:{queryHash}");

    /// <summary>A page of <c>GET /api/products?lowStock=true</c> (paged, so the key carries the query hash).</summary>
    public static string LowStock(Guid shopId, long version, string queryHash) =>
        string.Create(CultureInfo.InvariantCulture, $"stockly:{shopId:D}:products:lowstock:v{version}:{queryHash}");
}
