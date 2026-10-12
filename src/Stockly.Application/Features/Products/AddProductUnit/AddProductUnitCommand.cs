using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.AddProductUnit;

/// <summary>P6: adds a unit to the product. Its barcode must be unused by every unit of the shop.</summary>
public sealed record AddProductUnitCommand(Guid ProductId, string Name, string Factor, string SellPrice, string? Barcode)
    : ICommand<UnitDto>, IInvalidatesCatalog, IProductUnitInput;
