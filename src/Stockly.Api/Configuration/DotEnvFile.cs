namespace Stockly.Api.Configuration;

/// <summary>
/// Minimal reader for the repo-root <c>.env</c> used in local development: <c>KEY=VALUE</c> per line, <c>#</c>
/// comments, optional matching quotes around the value. The value is everything after the first <c>=</c>.
/// Errors name the line number, never the line, because lines hold secrets.
/// </summary>
public static class DotEnvFile
{
    public const string FileName = ".env";
    private const string RepositoryMarker = "Stockly.sln";

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            var key = separator > 0 ? line[..separator].Trim() : string.Empty;
            if (key.Length == 0)
            {
                throw new FormatException($"{FileName}: line {lineNumber} is not KEY=VALUE.");
            }

            values[key] = Unquote(line[(separator + 1)..].Trim());
        }

        return values;
    }

    /// <summary>Walks up from <paramref name="startDirectory"/> to the folder holding Stockly.sln and returns its .env, if any.</summary>
    public static string? FindInRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarker)))
            {
                var candidate = Path.Combine(directory.FullName, FileName);
                return File.Exists(candidate) ? candidate : null;
            }
        }

        return null;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;
}
