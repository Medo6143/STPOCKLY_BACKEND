using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests;

/// <summary>
/// docs/api/openapi.json is the machine-readable contract for stockly-mobile (ADR-0011). It is generated here,
/// never by the build, so a build never starts the host or needs configuration. When the committed file is out
/// of date, this test rewrites it and fails: review the diff and commit the file with your change.
/// </summary>
public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task CommittedSpec_MatchesRuntimeDocument()
    {
        var expected = OpenApiSpec.Serialize(await OpenApiSpec.LoadRuntimeAsync());
        var committed = File.Exists(OpenApiSpec.CommittedFile)
            ? (await File.ReadAllTextAsync(OpenApiSpec.CommittedFile)).ReplaceLineEndings("\n")
            : null;

        if (committed != expected)
        {
            await File.WriteAllTextAsync(OpenApiSpec.CommittedFile, expected);
        }

        committed.Should().Be(
            expected,
            "{0} was out of date and has been regenerated; review the diff and commit it",
            OpenApiSpec.CommittedPath);
    }
}
