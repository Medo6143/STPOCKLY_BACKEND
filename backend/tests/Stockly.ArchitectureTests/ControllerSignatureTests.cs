using Stockly.ArchitectureTests.Fixtures;
using Stockly.ArchitectureTests.Fixtures.ForbiddenModel;

namespace Stockly.ArchitectureTests;

/// <summary>Api controllers expose Contracts DTOs, never Domain entities (AGENTS.md rule 27).</summary>
public sealed class ControllerSignatureTests
{
    private const string FixtureForbiddenNamespace = "Stockly.ArchitectureTests.Fixtures.ForbiddenModel";

    [Fact]
    public void ApiControllers_PublicSignatures_DoNotExposeDomainTypes()
    {
        var usages = ControllerSignatureInspector.FindForbiddenUsages(
            Layers.Load(Layers.Api).GetTypes(),
            Layers.Domain);

        usages.Should().BeEmpty("controllers return records from Stockly.Contracts, never Domain entities");
    }

    [Fact]
    public void FindForbiddenUsages_ForbiddenTypeInReturnOrParameter_ReportsEachAction()
    {
        var usages = ControllerSignatureInspector.FindForbiddenUsages(
            [typeof(LeakingController)],
            FixtureForbiddenNamespace);

        usages.Should().BeEquivalentTo(
        [
            $"{nameof(LeakingController)}.{nameof(LeakingController.GetAsync)} exposes {typeof(ForbiddenEntity).FullName}",
            $"{nameof(LeakingController)}.{nameof(LeakingController.Update)} exposes {typeof(ForbiddenEntity).FullName}",
            $"{nameof(LeakingController)}.{nameof(LeakingController.ListArray)} exposes {typeof(ForbiddenEntity).FullName}",
        ]);
    }

    [Fact]
    public void FindForbiddenUsages_OnlyAllowedTypes_ReportsNothing()
    {
        var usages = ControllerSignatureInspector.FindForbiddenUsages(
            [typeof(CleanController)],
            FixtureForbiddenNamespace);

        usages.Should().BeEmpty();
    }
}
