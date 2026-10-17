using Stockly.Application.Abstractions.Messaging;

namespace Stockly.Application.Features.Admin.ActivateShop;

/// <summary>AD7: reactivates a suspended shop. Idempotent: an active shop changes nothing and writes no audit entry.</summary>
public sealed record ActivateShopCommand(Guid ShopId, string? Reason) : ICommand;
