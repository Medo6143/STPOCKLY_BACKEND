using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Stockly.Infrastructure.Identity;

/// <summary>
/// Access token settings (<c>Jwt__*</c>). <see cref="SigningKey"/> is a secret: it comes from <c>.env</c> or the
/// environment only, is at least <see cref="MinimumSigningKeyBytes"/> bytes and is never logged.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string DefaultIssuer = "stockly";
    public const string DefaultAudience = "stockly-app";

    /// <summary>HMAC-SHA256 needs a 256-bit key.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public required string SigningKey { get; init; }

    public string Issuer { get; init; } = DefaultIssuer;

    public string Audience { get; init; } = DefaultAudience;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>The validation side of the tokens <see cref="JwtTokenService"/> issues. No clock skew: the server validates its own tokens.</summary>
    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = Application.Abstractions.Identity.StocklyClaimTypes.Subject,
        RoleClaimType = Application.Abstractions.Identity.StocklyClaimTypes.Role,
    };

    /// <summary>Never prints the key.</summary>
    public override string ToString() => $"Issuer={Issuer}, Audience={Audience}";
}
