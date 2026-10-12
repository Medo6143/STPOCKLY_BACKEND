using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.UpdateProductUnit;

/// <summary>P7: replaces a unit's name, factor, sell price and barcode. The base unit's factor stays 1.</summary>
public sealed record UpdateProductUnitCommand(Guid ProductId, Guid UnitId, string Name, string Factor, string SellPrice, string? Barcode)
    : ICommand<UnitDto>, IInvalidatesCatalog, IProductUnitInput;
