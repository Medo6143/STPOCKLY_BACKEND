using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Auth;

namespace Stockly.Application.Features.Auth.GetMe;

/// <summary>A5: the user and the token's active shop with the user's role in it.</summary>
public sealed record GetMeQuery : IQuery<MeResponse>;
