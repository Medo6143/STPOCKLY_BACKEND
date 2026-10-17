using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.AdminRefresh;

/// <summary>
/// AD2: rotates an admin refresh token. Reuse of a revoked token revokes its whole family; that revocation is committed
/// even though the command fails (<see cref="ICommitOnFailure"/>).
/// </summary>
public sealed record AdminRefreshCommand(string RefreshToken) : ICommand<AdminAuthResponse>, ICommitOnFailure;
