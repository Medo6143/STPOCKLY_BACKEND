namespace Stockly.Application.Abstractions.Identity;

/// <summary>Claim names in Stockly access tokens. Shared by the token issuer and the readers (ADR-0002).</summary>
public static class StocklyClaimTypes
{
    /// <summary>The active shop of the token. One per token; switching shop issues a new token.</summary>
    public const string ShopId = "shop_id";
}
