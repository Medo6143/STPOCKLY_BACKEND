using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Auth;

namespace Stockly.Application.Features.Auth.Register;

/// <summary>A1: creates the user, the shop, the Owner membership and the default plan subscription in one transaction.</summary>
public sealed record RegisterCommand(
    string FullName,
    string Email,
    string Password,
    string? Phone,
    string? PreferredLanguage,
    string? DeviceId,
    RegisterShopRequest Shop) : ICommand<AuthResponse>;
