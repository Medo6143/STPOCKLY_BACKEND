using System.Text;
using Stockly.Infrastructure.Identity;

namespace Stockly.Api.Configuration;

public static class ConnectionStringNames
{
    public const string Postgres = "Postgres";
    public const string Redis = "Redis";
}

public static class RequiredConfiguration
{
    private const string SigningKeyVariable = "Jwt__SigningKey";

    /// <summary>Fails startup with the variable to set. Never includes configuration values (they are secrets).</summary>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name)
    {
        var value = configuration.GetConnectionString(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Connection string '{name}' is missing. Set the environment variable ConnectionStrings__{name} " +
                "(locally: copy .env.example to .env).")
            : value;
    }

    /// <summary>
    /// <c>Jwt:SigningKey</c> is required and at least <see cref="JwtOptions.MinimumSigningKeyBytes"/> bytes (UTF-8).
    /// <c>Jwt:Issuer</c> and <c>Jwt:Audience</c> default to <c>stockly</c> / <c>stockly-app</c>. Errors never show the key.
    /// </summary>
    public static JwtOptions GetRequiredJwtOptions(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(JwtOptions.SectionName);
        var signingKey = section[nameof(JwtOptions.SigningKey)];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                $"The JWT signing key is missing. Set the environment variable {SigningKeyVariable} (locally: copy .env.example to .env).");
        }

        if (Encoding.UTF8.GetByteCount(signingKey) < JwtOptions.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"The JWT signing key is too short. {SigningKeyVariable} must be at least {JwtOptions.MinimumSigningKeyBytes} bytes of random data.");
        }

        return new JwtOptions
        {
            SigningKey = signingKey,
            Issuer = NonBlankOr(section[nameof(JwtOptions.Issuer)], JwtOptions.DefaultIssuer),
            Audience = NonBlankOr(section[nameof(JwtOptions.Audience)], JwtOptions.DefaultAudience),
        };
    }

    private static string NonBlankOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
