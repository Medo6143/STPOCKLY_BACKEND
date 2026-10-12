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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Startup_MissingJwtSigningKey_FailsWithVariableName(string signingKey)
    {
        await using var factory = fixture.CreateFactory(jwtSigningKey: signingKey);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt__SigningKey*");
    }

    [Fact]
    public async Task Startup_ShortJwtSigningKey_FailsWithoutShowingTheKey()
    {
        const string shortKey = "only-31-bytes-of-signing-key-xx";
        await using var factory = fixture.CreateFactory(jwtSigningKey: shortKey);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt__SigningKey*32 bytes*")
            .Which.Message.Should().NotContain(shortKey);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("ten")]
    public async Task Startup_InvalidAuthPermitLimit_FailsWithVariableName(string permitLimit)
    {
        await using var factory = fixture.CreateFactory(authPermitLimit: permitLimit);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RateLimiting__Auth__PermitLimit*");
    }
}
