using System.Text.Json.Nodes;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests;

/// <summary>
/// ADR-0011: docs/api/openapi.json is the contract stockly-mobile builds against. Local builds regenerate it;
/// CI skips generation (CI=true), so this test fails there when a change was not committed with its spec.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class OpenApiContractTests(ApiFixture fixture)
{
    private const string CommittedSpec = "docs/api/openapi.json";

    [Fact]
    public async Task CommittedSpec_MatchesRuntimeDocument()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var runtime = Normalize(JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)))!);
        var committed = Normalize(JsonNode.Parse(await File.ReadAllTextAsync(ComposeImages.RepoFile(CommittedSpec)))!);

        JsonNode.DeepEquals(runtime, committed).Should().BeTrue(
            $"{CommittedSpec} is out of date: build src/Stockly.Api locally (it regenerates the file) and commit it");
    }

    // "servers" holds the host the document was served from; it differs between build time and runtime.
    private static JsonNode Normalize(JsonNode document)
    {
        document.AsObject().Remove("servers");
        return document;
    }
}
