using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.CountStock;

/// <summary>
/// P10 (Owner only, enforced by the Api policy): a physical count in the unit <see cref="UnitId"/> (null = base). A
/// count equal to the current quantity records no movement.
/// </summary>
public sealed record CountStockCommand(Guid ProductId, string CountedQuantity, Guid? UnitId)
    : ICommand<StockCountResponse>, IInvalidatesCatalog;
