using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Common;

namespace Stockly.Application.Behaviors;

/// <summary>
/// Runs outside <see cref="TransactionBehavior{TRequest, TResponse}"/>: when a catalog write succeeded, its
/// transaction has already committed, and only then is <c>stockly:{shopId}:ver:catalog</c> bumped (AGENTS.md rule 23;
/// bumping before commit would let a reader re-cache old data). A failed write rolled back and bumps nothing.
/// <see cref="ICacheService"/> never throws on a Redis failure, so the write still succeeds (rule 25).
/// </summary>
public sealed class CatalogCacheInvalidationBehavior<TRequest, TResponse>(ICacheService cache, ICurrentTenant currentTenant)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IInvalidatesCatalog
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var response = await next(cancellationToken);
        if (response.IsSuccess && currentTenant.ShopId is { } shopId)
        {
            await cache.IncrementVersionAsync(CacheKeys.CatalogVersion(shopId), AfterCommit.Token);
        }

        return response;
    }
}
