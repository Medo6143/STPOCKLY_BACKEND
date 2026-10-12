using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Auth;

namespace Stockly.Application.Features.Auth.Login;

/// <summary>
/// A2: signs in to the active membership joined first and starts a new refresh token family. Commits on failure: a
/// failed attempt must be counted (account lockout) although the command returns <c>Auth.InvalidCredentials</c>.
/// </summary>
public sealed record LoginCommand(string Email, string Password, string? DeviceId) : ICommand<AuthResponse>, ICommitOnFailure;
