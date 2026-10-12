using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.AdjustStock;

/// <summary>
/// P9 (Owner only, enforced by the Api policy): a manual correction recorded as a new <c>ManualAdjustment</c> movement
/// (nothing is edited, AGENTS.md rule 2). <see cref="QuantityChange"/> is in the unit <see cref="UnitId"/> (null = base).
/// </summary>
public sealed record AdjustStockCommand(Guid ProductId, string QuantityChange, string? Note, Guid? UnitId)
    : ICommand<StockMovementDto>, IInvalidatesCatalog;
