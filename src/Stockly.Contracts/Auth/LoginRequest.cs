namespace Stockly.Contracts.Auth;

/// <summary>A2 <c>POST /api/auth/login</c>.</summary>
public sealed record LoginRequest(string Email, string Password, string? DeviceId = null);
