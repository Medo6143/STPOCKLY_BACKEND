using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Stockly.Api.Configuration;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Security;

/// <summary>
/// <c>Ai__UseFake</c> (load test only, docs/operations/load-test.md): refused outside the <c>LoadTest</c> environment,
/// and in it a scan goes through the real extractor and job with the fake OpenRouter handler, never the network.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class FakeAiGuardTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("Production")]
    [InlineData(StocklyApiFactory.TestEnvironment)]
    public async Task Startup_FakeAiOutsideLoadTest_Fails(string environment)
    {
        await using var factory = fixture.CreateFactory(environment: environment, settings: FakeAiSetting("true"));

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Ai__UseFake*LoadTest*");
    }

    [Theory]
    [InlineData("Development", "true", null)]
    [InlineData("Staging", "true", null)]
    [InlineData("loadtest", "true", true)]
    [InlineData("LoadTest", "True", true)]
    [InlineData("LoadTest", "false", false)]
    [InlineData("Production", "", false)]
    [InlineData("Production", null, false)]
    public void IsEnabled_OnlyInTheLoadTestEnvironment(string environment, string? value, bool? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FakeAi.Key] = value })
            .Build();
        var host = new HostingEnvironment { EnvironmentName = environment };

        var act = () => FakeAi.IsEnabled(configuration, host);

        if (expected is { } enabled)
        {
            act().Should().Be(enabled);
        }
        else
        {
            act.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task Startup_FakeAiNotABoolean_Fails()
    {
        await using var factory = fixture.CreateFactory(environment: "LoadTest", settings: FakeAiSetting("maybe"));

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Ai__UseFake*true or false*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData(StocklyApiFactory.TestEnvironment)]
    public async Task Startup_FakeAiFalse_StartsAnywhere(string environment)
    {
        await using var factory = fixture.CreateFactory(environment: environment, settings: FakeAiSetting("false"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LoadTest_FakeAi_ScanBecomesNeedsReviewWithMatchedLines()
    {
        await using var factory = fixture.CreateFactory(
            environment: "LoadTest", settings: FakeAiSetting("true"), replaceExtractor: false, jobs: StocklyApiFactory.JobMode.Run);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var panadol = await ProductApi.CreateAsync(client, session.AccessToken, ProductApi.CreateBody(name: "Panadol Extra"));

        var id = await InvoiceApi.ScanAsync(client, session.AccessToken);
        var processed = await InvoiceApi.PollUntilProcessedAsync(client, id, session.AccessToken, TimeSpan.FromSeconds(60));

        processed.Body!["status"]!.GetValue<string>().Should().Be("NeedsReview", processed.Raw);
        var items = processed.Body["items"]!.AsArray();
        items.Should().HaveCount(3);
        items[0]!["productId"]!.GetValue<Guid>().Should().Be(panadol, "\"بنادول اكسترا\" matches \"Panadol Extra\" across scripts");
        items[0]!["matchConfidence"].Should().NotBeNull();
        factory.Extractor.Calls.Should().Be(0, "the real extractor ran, with the fake OpenRouter handler");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM ai_usage_records WHERE invoice_id = @p0", id)).Should().Be(1);
    }

    private static Dictionary<string, string?> FakeAiSetting(string value) => new() { [FakeAi.Key] = value };
}
