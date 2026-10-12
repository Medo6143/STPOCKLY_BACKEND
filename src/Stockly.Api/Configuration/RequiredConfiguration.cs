namespace Stockly.Api.Configuration;

public static class ConnectionStringNames
{
    public const string Postgres = "Postgres";
    public const string Redis = "Redis";
}

public static class RequiredConfiguration
{
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
}
