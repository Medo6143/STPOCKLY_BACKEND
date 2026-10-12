using Microsoft.AspNetCore.Authorization;

namespace Stockly.Api.Authentication;

/// <summary>The token's shop exists and is not suspended. Checked on every authenticated request, except logout.</summary>
public sealed class ActiveShopRequirement : IAuthorizationRequirement;
