using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;

namespace Stockly.Application.Features.Products.RemoveProductUnit;

/// <summary>P8: deletes a unit that is not the base unit.</summary>
public sealed record RemoveProductUnitCommand(Guid ProductId, Guid UnitId) : ICommand, IInvalidatesCatalog;
