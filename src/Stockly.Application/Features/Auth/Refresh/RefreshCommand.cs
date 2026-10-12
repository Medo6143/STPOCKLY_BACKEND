using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Auth;

namespace Stockly.Application.Features.Auth.Refresh;

/// <summary>
/// A3: rotates the refresh token. Reuse of a revoked token revokes its whole family; that revocation is committed
/// even though the command fails (<see cref="ICommitOnFailure"/>).
/// </summary>
public sealed record RefreshCommand(string RefreshToken) : ICommand<AuthResponse>, ICommitOnFailure;
