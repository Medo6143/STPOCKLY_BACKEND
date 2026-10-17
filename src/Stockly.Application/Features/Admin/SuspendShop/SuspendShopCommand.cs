using Stockly.Application.Abstractions.Messaging;

namespace Stockly.Application.Features.Admin.SuspendShop;

/// <summary>
/// AD6: suspends a shop and revokes its members' refresh tokens; their next call gets <c>403 Shop.Suspended</c>.
/// Idempotent: an already suspended shop changes nothing and writes no audit entry.
/// </summary>
public sealed record SuspendShopCommand(Guid ShopId, string? Reason) : ICommand;
