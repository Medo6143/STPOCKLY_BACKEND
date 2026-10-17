using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.AdminLogin;

/// <summary>
/// AD1: signs a platform admin in and starts a new refresh token family. Commits on failure: a failed attempt must be
/// counted (account lockout) although the command returns <c>Auth.InvalidCredentials</c>.
/// </summary>
public sealed record AdminLoginCommand(string Email, string Password) : ICommand<AdminAuthResponse>, ICommitOnFailure;
