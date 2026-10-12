using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Auth;

namespace Stockly.Application.Features.Auth.Login;

/// <summary>A2: signs in to the active membership joined first and starts a new refresh token family.</summary>
public sealed record LoginCommand(string Email, string Password, string? DeviceId) : ICommand<AuthResponse>;
