namespace Stockly.Contracts.Auth;

/// <summary>A5 <c>GET /api/auth/me</c>: restores the session at app start-up.</summary>
public sealed record MeResponse(UserDto User, ShopMembershipDto Shop);
