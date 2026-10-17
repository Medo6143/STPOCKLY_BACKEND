namespace Stockly.Application.Abstractions.Identity;

/// <summary>Claim names in Stockly access tokens. Shared by the token issuer and the readers (ADR-0002).</summary>
public static class StocklyClaimTypes
{
    /// <summary>The user id (JWT registered claim).</summary>
    public const string Subject = "sub";

    /// <summary>The active shop of the token. One per token; switching shop issues a new token.</summary>
    public const string ShopId = "shop_id";

    /// <summary>The user's <c>ShopRole</c> in the active shop: <c>Owner</c> or <c>Staff</c>.</summary>
    public const string Role = "role";

    /// <summary>
    /// Platform role of an admin token (contract §11.1): <see cref="PlatformAdminValue"/>. An admin token has no
    /// <see cref="ShopId"/> claim.
    /// </summary>
    public const string PlatformRole = "platform_role";

    /// <summary>The <see cref="PlatformRole"/> value of a platform admin.</summary>
    public const string PlatformAdminValue = "PlatformAdmin";

    /// <summary>Unique token id (JWT registered claim).</summary>
    public const string TokenId = "jti";
}
