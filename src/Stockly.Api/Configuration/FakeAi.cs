namespace Stockly.Api.Configuration;

/// <summary>
/// <c>Ai__UseFake</c>: the load test's stand-in for OpenRouter (docs/operations/load-test.md). Allowed only in the
/// <see cref="LoadTestEnvironment"/> environment; anywhere else, Production included, start-up fails, because every scan
/// would get a fake extraction.
/// </summary>
public static class FakeAi
{
    public const string Key = "Ai:UseFake";
    public const string LoadTestEnvironment = "LoadTest";

    private const string Variable = "Ai__UseFake";

    /// <summary>Whether the fake AI is on. Throws when it is on outside <see cref="LoadTestEnvironment"/> or not a boolean.</summary>
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var value = configuration[Key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var enabled))
        {
            throw new InvalidOperationException($"{Variable} must be true or false.");
        }

        if (enabled && !environment.IsEnvironment(LoadTestEnvironment))
        {
            throw new InvalidOperationException(
                $"{Variable}=true is allowed only when ASPNETCORE_ENVIRONMENT is {LoadTestEnvironment}: it answers every scan with a fake extraction.");
        }

        return enabled;
    }
}
