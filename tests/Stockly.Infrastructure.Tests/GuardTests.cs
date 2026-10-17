using System.Text.RegularExpressions;
using Stockly.Infrastructure.Tests.Support;
using Stockly.Infrastructure.Time;

namespace Stockly.Infrastructure.Tests;

public sealed partial class GuardTests
{
    [Fact]
    public void PostgresImage_MatchesDockerCompose()
    {
        var compose = File.ReadAllText(FindRepoFile("docker-compose.yml"));

        var composeImage = PostgresImageLine().Match(compose).Groups["image"].Value;

        composeImage.Should().Be(PostgresFixture.PostgresImage, "tests must run against the same pinned image as development");
    }

    [Fact]
    public void RedisImage_MatchesDockerCompose()
    {
        var compose = File.ReadAllText(FindRepoFile("docker-compose.yml"));

        var composeImage = RedisImageLine().Match(compose).Groups["image"].Value;

        composeImage.Should().Be(RedisFixture.RedisImage, "tests must run against the same pinned image as development");
    }

    [Fact]
    public void MinioImage_MatchesDockerCompose()
    {
        var compose = File.ReadAllText(FindRepoFile("docker-compose.yml"));

        var composeImage = MinioImageLine().Match(compose).Groups["image"].Value;

        composeImage.Should().Be(MinioFixture.MinioImage, "tests must run against the same pinned image as development");
    }

    [Fact]
    public void SystemClock_UtcNow_HasZeroOffset()
    {
        var clock = new SystemClock();

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero, "Npgsql rejects non-UTC DateTimeOffset values for timestamptz");
    }

    [Fact]
    public void SystemClock_SuccessiveReads_DoNotGoBackwards()
    {
        var clock = new SystemClock();

        var first = clock.UtcNow;
        var second = clock.UtcNow;

        second.Should().BeOnOrAfter(first);
    }

    private static string FindRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, fileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException($"{fileName} not found above {AppContext.BaseDirectory}")
            : Path.Combine(directory.FullName, fileName);
    }

    [GeneratedRegex(@"image:\s*(?<image>postgres:\S+)")]
    private static partial Regex PostgresImageLine();

    [GeneratedRegex(@"image:\s*(?<image>redis:\S+)")]
    private static partial Regex RedisImageLine();

    [GeneratedRegex(@"image:\s*(?<image>pgsty/minio:\S+)")]
    private static partial Regex MinioImageLine();
}
