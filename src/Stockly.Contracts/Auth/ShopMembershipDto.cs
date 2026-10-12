using Stockly.Contracts.Shops;

namespace Stockly.Contracts.Auth;

/// <summary>The active shop of a token and the user's role in it.</summary>
public sealed record ShopMembershipDto(Guid Id, string Name, ShopType ShopType, ShopStatus Status, ShopRole Role);
