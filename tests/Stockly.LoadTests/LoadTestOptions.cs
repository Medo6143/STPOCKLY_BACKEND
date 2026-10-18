using System.Globalization;
using System.Net;

namespace Stockly.LoadTests;

/// <summary>
/// Command-line options: <c>--base-url</c> (default <c>http://127.0.0.1:5090</c>, loopback only unless
/// <c>--allow-remote</c>), <c>--duration</c> seconds per timed scenario (30), <c>--concurrency</c> virtual users (20),
/// <c>--products</c> created for P1 and S1 (50), <c>--scans</c> for I1 (100), <c>--image-kb</c> per scan (200),
/// <c>--scenarios</c> to run (comma-separated, default <c>p1,s1,i1,i3</c>; I3 polls the invoices of I1).
/// </summary>
internal sealed record LoadTestOptions(
    Uri BaseUrl, TimeSpan Duration, int Concurrency, int Products, int Scans, int ImageKilobytes, IReadOnlySet<string> Scenarios)
{
    private static readonly string[] AllScenarios = ["p1", "s1", "i1", "i3"];

    public bool Runs(string scenario) => Scenarios.Contains(scenario);

    public static LoadTestOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowRemote = false;
        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (name == "--allow-remote")
            {
                allowRemote = true;
                continue;
            }

            if (!name.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
            {
                throw new ArgumentException($"Unexpected argument '{name}'. Usage: --base-url URL --duration S --concurrency N --products N --scans N --image-kb N --scenarios p1,s1,i1,i3 [--allow-remote]");
            }

            values[name[2..]] = args[++index];
        }

        var baseUrl = new Uri(values.GetValueOrDefault("base-url", "http://127.0.0.1:5090"), UriKind.Absolute);
        if (!allowRemote && !IsLoopback(baseUrl))
        {
            throw new ArgumentException("The load test writes data. It runs only against a loopback Api unless --allow-remote is given.");
        }

        var scenarios = values.GetValueOrDefault("scenarios", string.Join(',', AllScenarios))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (scenarios.Count == 0 || !scenarios.IsSubsetOf(AllScenarios))
        {
            throw new ArgumentException("--scenarios must be a comma-separated list of p1, s1, i1, i3.");
        }

        return new LoadTestOptions(
            baseUrl,
            TimeSpan.FromSeconds(Number(values, "duration", 30, 1, 3600)),
            Number(values, "concurrency", 20, 1, 500),
            Number(values, "products", 50, 1, 1000),
            Number(values, "scans", 100, 1, 10_000),
            Number(values, "image-kb", 200, 1, 8 * 1024),
            scenarios);
    }

    private static bool IsLoopback(Uri url) =>
        url.IsLoopback || (IPAddress.TryParse(url.Host, out var address) && IPAddress.IsLoopback(address));

    private static int Number(Dictionary<string, string> values, string name, int fallback, int min, int max)
    {
        if (!values.TryGetValue(name, out var text))
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? value
            : throw new ArgumentException($"--{name} must be a whole number from {min} to {max}.");
    }
}
