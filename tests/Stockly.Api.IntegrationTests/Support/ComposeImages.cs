using System.Text.RegularExpressions;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Reads the pinned images from docker-compose.yml so tests always use the development images.</summary>
internal static partial class ComposeImages
{
    private static readonly Lazy<string> Compose = new(() => File.ReadAllText(RepoFile("docker-compose.yml")));

    public static string Postgres => Find(PostgresImage());

    public static string Redis => Find(RedisImage());

    public static string RepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Stockly.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException($"Stockly.sln not found above {AppContext.BaseDirectory}")
            : Path.Combine(directory.FullName, relativePath);
    }

    private static string Find(Regex pattern)
    {
        var match = pattern.Match(Compose.Value);
        return match.Success
            ? match.Groups["image"].Value
            : throw new InvalidOperationException($"Image not found in docker-compose.yml: {pattern}");
    }

    [GeneratedRegex(@"image:\s*(?<image>postgres:\S+)")]
    private static partial Regex PostgresImage();

    [GeneratedRegex(@"image:\s*(?<image>redis:\S+)")]
    private static partial Regex RedisImage();
}
