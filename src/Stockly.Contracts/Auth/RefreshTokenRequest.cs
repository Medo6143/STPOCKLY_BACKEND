namespace Stockly.Contracts.Auth;

/// <summary>A3 <c>POST /api/auth/refresh</c> and A4 <c>POST /api/auth/logout</c>.</summary>
public sealed record RefreshTokenRequest(string RefreshToken);
