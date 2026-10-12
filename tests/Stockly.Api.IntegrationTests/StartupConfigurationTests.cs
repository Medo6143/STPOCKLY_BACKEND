using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class StartupConfigurationTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("Postgres")]
    [InlineData("Redis")]
    public async Task Startup_MissingConnectionString_FailsWithVariableName(string name)
    {
        await using var factory = name == "Postgres"
            ? fixture.CreateFactory(postgres: " ")
            : fixture.CreateFactory(redis: "");

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*ConnectionStrings__{name}*")
            .Which.Message.Should().NotContain(fixture.PostgresConnectionString).And.NotContain(fixture.RedisConnectionString);
    }
}
