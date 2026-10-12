using Microsoft.Extensions.Configuration;
using Stockly.Api.Configuration;

namespace Stockly.Api.IntegrationTests.Configuration;

public sealed class DotEnvFileTests
{
    [Fact]
    public void Parse_TypicalFile_ReadsKeysAndValues()
    {
        string[] lines =
        [
            "# comment",
            "",
            "   ",
            "POSTGRES_USER=stockly",
            "ConnectionStrings__Postgres=Host=localhost;Port=5432;Password=a=b",
            "  SPACED  =  value with spaces  ",
            "QUOTED=\"double quoted\"",
            "SINGLE='single quoted'",
            "EMPTY=",
        ];

        var values = DotEnvFile.Parse(lines);

        values.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["POSTGRES_USER"] = "stockly",
            ["ConnectionStrings__Postgres"] = "Host=localhost;Port=5432;Password=a=b",
            ["SPACED"] = "value with spaces",
            ["QUOTED"] = "double quoted",
            ["SINGLE"] = "single quoted",
            ["EMPTY"] = string.Empty,
        });
    }

    [Theory]
    [InlineData("NO_EQUALS_SIGN")]
    [InlineData("=value-without-key")]
    public void Parse_InvalidLine_ThrowsWithLineNumberButNotContent(string invalid)
    {
        string[] lines = ["OK=1", invalid];

        var act = () => DotEnvFile.Parse(lines);

        act.Should().Throw<FormatException>()
            .WithMessage("*line 2*")
            .Which.Message.Should().NotContain(invalid);
    }

    [Fact]
    public void Parse_LaterDuplicate_Wins()
    {
        DotEnvFile.Parse(["KEY=first", "KEY=second"])["KEY"].Should().Be("second");
    }

    [Fact]
    public void FindInRepositoryRoot_FromNestedFolder_FindsEnvNextToSolution()
    {
        using var repo = new TempRepository(withEnv: true);

        var found = DotEnvFile.FindInRepositoryRoot(repo.Nested);

        found.Should().Be(Path.Combine(repo.Root, ".env"));
    }

    [Fact]
    public void FindInRepositoryRoot_NoEnvFile_ReturnsNull()
    {
        using var repo = new TempRepository(withEnv: false);

        DotEnvFile.FindInRepositoryRoot(repo.Nested).Should().BeNull();
    }

    [Fact]
    public void AddDotEnvBelowEnvironmentVariables_EnvironmentVariableWins_DotEnvBeatsAppSettings()
    {
        var key = $"StocklyDotEnvTest{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable($"{key}__FromEnv", "environment");
        try
        {
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{key}:FromEnv"] = "appsettings",
                    [$"{key}:FromDotEnv"] = "appsettings",
                    [$"{key}:AppOnly"] = "appsettings",
                })
                .AddEnvironmentVariables();

            builder.AddDotEnvBelowEnvironmentVariables(new Dictionary<string, string>
            {
                [$"{key}__FromEnv"] = "dotenv",
                [$"{key}__FromDotEnv"] = "dotenv",
            });
            var configuration = builder.Build();

            configuration[$"{key}:FromEnv"].Should().Be("environment");
            configuration[$"{key}:FromDotEnv"].Should().Be("dotenv");
            configuration[$"{key}:AppOnly"].Should().Be("appsettings");
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{key}__FromEnv", null);
        }
    }

    [Fact]
    public void AddRepositoryDotEnv_EnvFileAtRepoRoot_LoadsValuesWithDoubleUnderscoreAsSection()
    {
        using var repo = new TempRepository(withEnv: true, content: "ConnectionStrings__Postgres=Host=db");
        var builder = new ConfigurationBuilder();

        builder.AddRepositoryDotEnv(repo.Nested);

        builder.Build().GetConnectionString("Postgres").Should().Be("Host=db");
    }

    [Fact]
    public void AddRepositoryDotEnv_NoEnvFile_AddsNothing()
    {
        using var repo = new TempRepository(withEnv: false);
        var builder = new ConfigurationBuilder();

        builder.AddRepositoryDotEnv(repo.Nested);

        builder.Sources.Should().BeEmpty();
    }

    [Fact]
    public void AddDotEnvBelowEnvironmentVariables_NoEnvironmentSource_AppendsAtEnd()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Key"] = "appsettings" });

        builder.AddDotEnvBelowEnvironmentVariables(new Dictionary<string, string> { ["Key"] = "dotenv" });

        builder.Build()["Key"].Should().Be("dotenv");
    }

    private sealed class TempRepository : IDisposable
    {
        public TempRepository(bool withEnv, string content = "KEY=value")
        {
            Root = Path.Combine(Path.GetTempPath(), $"stockly-dotenv-{Guid.NewGuid():N}");
            Nested = Path.Combine(Root, "src", "Stockly.Api");
            Directory.CreateDirectory(Nested);
            File.WriteAllText(Path.Combine(Root, "Stockly.sln"), string.Empty);
            if (withEnv)
            {
                File.WriteAllText(Path.Combine(Root, ".env"), content);
            }
        }

        public string Root { get; }

        public string Nested { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
