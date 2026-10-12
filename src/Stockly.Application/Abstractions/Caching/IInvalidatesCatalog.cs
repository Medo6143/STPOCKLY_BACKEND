namespace Stockly.Application.Abstractions.Caching;

/// <summary>
/// A command that writes products, units or stock. When it succeeds, <c>CatalogCacheInvalidationBehavior</c> bumps
/// <c>stockly:{shopId}:ver:catalog</c> after the transaction has committed (AGENTS.md rule 23).
/// </summary>
public interface IInvalidatesCatalog;
