using Stockly.Application.Abstractions.Messaging;

namespace Stockly.Application.Features.Auth.Logout;

/// <summary>A4: revokes the presented token's whole family. Idempotent: unknown or revoked tokens still succeed.</summary>
public sealed record LogoutCommand(string RefreshToken) : ICommand;
