using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products;

/// <summary>
/// Cache-aside for the P1 list pages, keyed by the shop's catalog version (docs/design/redis.md §2). When the version
/// cannot be read (Redis down) the cache is skipped entirely, so a stale page is never served.
/// </summary>
public sealed class CatalogListCache(ICacheService cache, ICurrentTenant currentTenant, IProductQueries queries)
{
    private const int QueryHashLength = 32;

    public async Task<PagedResponse<ProductListItem>> GetOrLoadAsync(
        ProductListFilter filter,
        Func<Guid, long, string, string> keyFor,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(keyFor);

        if (currentTenant.ShopId is not { } shopId
            || await cache.GetVersionAsync(CacheKeys.CatalogVersion(shopId), cancellationToken) is not { } version)
        {
            return await queries.ListAsync(filter, cancellationToken);
        }

        var key = keyFor(shopId, version, QueryHash(filter));
        var cached = await cache.GetAsync<PagedResponse<ProductListItem>>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var page = await queries.ListAsync(filter, cancellationToken);
        await cache.SetAsync(key, page, timeToLive, cancellationToken);
        return page;
    }

    /// <summary>Stable hash of everything that shapes the page (lower-case hex, first 32 characters of SHA-256).</summary>
    public static string QueryHash(ProductListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"search={filter.NormalizedSearch}|low={filter.LowStockOnly}|archived={filter.IncludeArchived}|page={filter.Page}|size={filter.PageSize}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..QueryHashLength];
    }
}
