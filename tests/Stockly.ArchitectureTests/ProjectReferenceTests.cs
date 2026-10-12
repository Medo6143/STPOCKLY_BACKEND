namespace Stockly.ArchitectureTests;

/// <summary>Project and package references declared in each src .csproj (AGENTS.md §4, ADR-0009).</summary>
public sealed class ProjectReferenceTests
{
    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
    {
        [Layers.Domain] = [],
        [Layers.Contracts] = [],
        [Layers.Application] = [Layers.Domain, Layers.Contracts],
        [Layers.Infrastructure] = [Layers.Application],
        [Layers.Api] = [Layers.Application, Layers.Infrastructure, Layers.Contracts],
    };

    // Package prefixes Application must never reference (AGENTS.md §4).
    private static readonly string[] ForbiddenApplicationPackagePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "Microsoft.Extensions.Caching.StackExchangeRedis",
        "Hangfire",
        "AWSSDK",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Http",
    ];

    public static TheoryData<string> SourceProjects() => new(Layers.All);

    [Fact]
    public void SourceFolder_ContainsOnlyKnownProjects_EveryProjectHasRules()
    {
        var projectsOnDisk = Directory.GetDirectories(ProjectFile.SourceDirectory)
            .Select(Path.GetFileName);

        projectsOnDisk.Should().BeEquivalentTo(AllowedProjectReferences.Keys);
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void ProjectReferences_AreExactlyTheAllowedOnes(string project)
    {
        var projectFile = ProjectFile.Load(project);

        projectFile.ProjectReferences.Should().BeEquivalentTo(
            AllowedProjectReferences[project],
            "AGENTS.md §4 lists the exact references of {0}",
            project);
    }

    [Fact]
    public void Domain_PackageReferences_OnlyUuidNext()
    {
        var domain = ProjectFile.Load(Layers.Domain);

        domain.PackageReferences.Should().BeSubsetOf(["UUIDNext"], "ADR-0009 allows UUIDNext as Domain's only package");
    }

    [Fact]
    public void Contracts_PackageReferences_None()
    {
        var contracts = ProjectFile.Load(Layers.Contracts);

        contracts.PackageReferences.Should().BeEmpty("Contracts may reference nothing but the BCL");
    }

    [Fact]
    public void Application_PackageReferences_NoInfrastructurePackages()
    {
        var application = ProjectFile.Load(Layers.Application);

        var forbidden = application.PackageReferences
            .Where(package => ForbiddenApplicationPackagePrefixes.Any(
                prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

        forbidden.Should().BeEmpty("Application must not depend on infrastructure packages (AGENTS.md §4)");
    }
}
